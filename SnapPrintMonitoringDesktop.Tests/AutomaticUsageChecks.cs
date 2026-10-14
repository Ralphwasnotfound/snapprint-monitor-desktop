using System.Runtime.InteropServices;
using SnapPrintMonitoringDesktop;

internal static class AutomaticUsageChecks
{
    private sealed class Clock(DateTimeOffset at) : TimeProvider { public override DateTimeOffset GetUtcNow() => at; }
    private sealed class Source : ICompletedSheetUsageSource
    {
        public string SourceId => "SYNTHETIC_ONLY.ValidatedPhysicalReceipts";
        internal Dictionary<(string,uint),CompletedSheetReceipt> Receipts = new();
        public bool Supports(string printerName) => PaperInventoryService.PrinterNames.Contains(printerName);
        public CompletedSheetReceipt? Read(string printerName,string device,JobReading job) => Receipts.GetValueOrDefault((printerName,job.Id));
    }
    internal static void Run()
    {
        var dir=Directory.CreateTempSubdirectory("snapprint-auto-usage-tests-");
        try
        {
            int checks=0, sequence=0;
            void Check(bool condition,string reason) { if(!condition)throw new Exception(reason); checks++; }
            const string canon="Canon MG3000 series",epson="EPSON L3210 Series";
            var start = new DateTimeOffset(2026,10,14,0,0,0,TimeSpan.Zero);
            string Device(string name)=>@"USBPRINT\SYNTHETIC\"+name;
            JobReading Job(uint id,uint status,int submitted=2,uint pages=1,int? copies=1,string? duplex="SIMPLEX_REQUESTED") =>
                new(id,status,pages,status==128?pages:0){SubmittedAt=start.AddSeconds(submitted),CopiesRequested=copies,DuplexRequested=duplex};
            PrinterSnapshot Snapshot(int second,string printer=canon,params JobReading[] jobs)
            {
                return new(start.AddSeconds(second),PaperInventoryService.PrinterNames.Select(name=>
                {
                    var ownJobs=name==printer?jobs:Array.Empty<JobReading>();
                    var evidence=PrinterSignals.Classify(new("EXACT_QUEUE_DEVICE_ID",Device(name),true,8,0,"Synthetic fixture"),
                        new Dictionary<string,string>{["PrintQueue.NumberOfJobs"]=ownJobs.Length.ToString()},ownJobs,"AVAILABLE",ownJobs.Length>0?"PRINTING":"NORMAL_REPORTED");
                    return new PrinterReading(name,"UNKNOWN","Fixture"){Evidence=evidence};
                }).ToArray(),null);
            }
            PaperInventory Entry(PaperInventoryService service,string printer=canon)=>service.Current.Single(i=>i.PrinterName==printer);
            void Baseline(PaperInventoryService service,int sheets,string printer=canon,int second=1)
            {
                service.Observe(Snapshot(second-1));
                service.Adjust(printer,new(InventoryAction.SetCountedTotal,sheets,3,Entry(service,printer).Revision,Guid.NewGuid()),start.AddSeconds(second));
            }
            CompletedSheetReceipt Receipt(uint job,int sheets,string printer=canon,int submitted=2,int completed=4,SheetCompletionOutcome outcome=SheetCompletionOutcome.Completed)=>
                new("receipt-"+job,printer,Device(printer),job,start.AddSeconds(submitted),start.AddSeconds(completed),sheets,outcome);
            string PathFor()=>Path.Combine(dir.FullName,"inventory-"+(sequence++)+".json");
            // Positive cases require a synthetic adapter which attests actual physical sheet totals independently of Windows page metadata.
            foreach(var fixture in new[]{(Pages:1u,Copies:1,Duplex:"SIMPLEX_REQUESTED",Sheets:1,Label:"Single page"),
                (Pages:7u,Copies:1,Duplex:"SIMPLEX_REQUESTED",Sheets:7,Label:"Multiple pages"),
                (Pages:3u,Copies:2,Duplex:"SIMPLEX_REQUESTED",Sheets:6,Label:"Multiple copies"),
                (Pages:3u,Copies:2,Duplex:"LONG_EDGE_DUPLEX_REQUESTED",Sheets:4,Label:"Odd-page duplex copies"),
                (Pages:2u,Copies:1,Duplex:"SIMPLEX_REQUESTED",Sheets:2,Label:"Selected range rendered by application")})
            {
                var source=new Source(); source.Receipts[(canon,1)]=Receipt(1,fixture.Sheets);
                using var service=new PaperInventoryService(PathFor(),source:source,clock:new Clock(start));
                Baseline(service,20); service.Observe(Snapshot(3,canon,Job(1,16,pages:fixture.Pages,copies:fixture.Copies,duplex:fixture.Duplex)));
                service.Observe(Snapshot(4,canon,Job(1,128,pages:fixture.Pages,copies:fixture.Copies,duplex:fixture.Duplex)));
                Check(Entry(service).EstimatedSheetsRemaining==20-fixture.Sheets,fixture.Label+": only attested physical count is deducted");
                Check(!Entry(service).PhysicalCountVerified && Entry(service).LastAutomaticDeduction?.Sheets==fixture.Sheets,"Automatic output remains labeled unverified estimate");
            }
            // Real Windows flags/settings never grant physical authority, even if all requested metadata is present.
            using(var windows=new PaperInventoryService(PathFor(),clock:new Clock(start)))
            {
                Baseline(windows,10); windows.Observe(Snapshot(3,canon,Job(2,16,pages:7)));
                windows.Observe(Snapshot(4,canon,Job(2,128,pages:7)));
                Check(Entry(windows).EstimatedSheetsRemaining==10 && Entry(windows).ReconciliationRequired && !Entry(windows).AutomaticDeductionEnabled,"Production Windows completion does not authorize invented sheets");
                Check(Entry(windows).LastUsageDecision!.Reason.Contains("No validated"),"Diagnostics explain missing authority");
                windows.Observe(Snapshot(5));
                Check(Entry(windows).LastUsageDecision!.Reason.Contains("disappeared"),"Disappearance is not success");
            }
            foreach(var fixture in new[]{(Flags:128u|4u,Outcome:SheetCompletionOutcome.Completed,Pages:1u,Label:"Cancelled"),
                (Flags:128u|2u,Outcome:SheetCompletionOutcome.Completed,Pages:1u,Label:"Failed"),
                (Flags:128u,Outcome:SheetCompletionOutcome.Partial,Pages:1u,Label:"Partial receipt")})
            {
                var source=new Source();source.Receipts[(canon,3)]=Receipt(3,1,outcome:fixture.Outcome);
                using var service=new PaperInventoryService(PathFor(),source:source,clock:new Clock(start)); Baseline(service,10);
                service.Observe(Snapshot(3,canon,Job(3,16)));service.Observe(Snapshot(4,canon,Job(3,fixture.Flags)));
                Check(Entry(service).EstimatedSheetsRemaining==10 && Entry(service).ReconciliationRequired,fixture.Label+" preserves stock");
            }
            using(var windows=new PaperInventoryService(PathFor(),clock:new Clock(start)))
            {
                Baseline(windows,10);windows.Observe(Snapshot(3,canon,Job(4,16,pages:0,copies:null,duplex:null)));windows.Observe(Snapshot(4,canon,Job(4,128,pages:0,copies:null,duplex:null)));
                Check(Entry(windows).EstimatedSheetsRemaining==10,"Unknown pages/copies/duplex are never defaulted to one/simplex");
            }
            var ledgerPath=PathFor(); var receipts=new Source();receipts.Receipts[(epson,7)]=Receipt(7,7,epson);
            using(var service=new PaperInventoryService(ledgerPath,source:receipts,clock:new Clock(start)))
            {
                Baseline(service,10,epson);service.Observe(Snapshot(3,epson,Job(7,16,pages:7)));
                var low=service.Observe(Snapshot(4,epson,Job(7,128,pages:7)));
                using(var history=new PrintHistoryService(PathFor()))
                {
                    history.Observe(service.Attach(Snapshot(4,epson,Job(7,128,pages:7))));
                    Check(history.Current.Single().InventoryDeducted && history.Current.Single().EstimatedSheetsUsed==7 && !history.Current.Single().PhysicalCompletionVerified,"History reflects committed inventory receipt without inventing physical verification");
                    history.Observe(service.Attach(Snapshot(5,epson,Job(7,128,submitted:5,pages:7))));
                    Check(history.Current.Single(i=>i.SubmittedAt==start.AddSeconds(5)).EstimatedSheetsUsed is null,"Reused job ID cannot inherit an older inventory deduction");
                }
                Check(Entry(service,epson).SheetsLoaded==10 && Entry(service,epson).TotalEstimatedSheetsAccountedFor==7,"Summary uses original saved loaded amount and atomic accounted total");
                Check(Entry(service,epson).EstimatedSheetsRemaining==3 && low.Single().Kind==PaperNotificationKind.Low,"Trusted seven-sheet receipt: 10 to 3 triggers LOW");
                Check(service.Observe(Snapshot(5,epson,Job(7,128,pages:7))).Count==0 && Entry(service,epson).EstimatedSheetsRemaining==3,"Duplicate receipt does not deduct/notify again");
                receipts.Receipts[(epson,7)]=Receipt(7,7,epson) with{ReceiptId="changed-id-same-job"};
                service.Observe(Snapshot(6,epson,Job(7,128,pages:7)));
                Check(Entry(service,epson).EstimatedSheetsRemaining==3,"Changing receipt ID cannot double-account same job");
                service.Observe(Snapshot(7));
                receipts.Receipts[(epson,8)]=Receipt(8,3,epson,submitted:8,completed:10);
                service.Observe(Snapshot(9,epson,Job(8,16,submitted:8,pages:3)));
                var empty=service.Observe(Snapshot(10,epson,Job(8,128,submitted:8,pages:3)));
                Check(Entry(service,epson).TotalEstimatedSheetsAccountedFor==10,"Accounted totals follow eligible deductions without a separate counter");
                Check(Entry(service,epson).EstimatedSheetsRemaining==0 && empty.Single().Kind==PaperNotificationKind.Empty,"Next trusted three-sheet receipt: 3 to 0 triggers EMPTY");
                Check(Entry(service,canon).EstimatedSheetsRemaining is null,"Epson receipts never affect Canon");
            }
            using(var restart=new PaperInventoryService(ledgerPath,source:receipts,clock:new Clock(start.AddSeconds(20))))
            {
                Check(Entry(restart,epson).EstimatedSheetsRemaining==0 && Entry(restart,epson).AppliedUsageJobKeys.Count==2,"Deductions and job identity ledger survive restart");
                Check(restart.Observe(Snapshot(21,epson,Job(8,128,submitted:8,pages:3))).Count==0 && Entry(restart,epson).EstimatedSheetsRemaining==0,"Restart replay never double-deducts or repeats EMPTY");
            }
            foreach(var cause in new[]{"gap","manual","restart","missing-start","mapping","negative"})
            {
                var source=new Source();source.Receipts[(canon,9)]=Receipt(9,cause=="negative"?11:1);
                using var service=new PaperInventoryService(PathFor(),source:source,clock:new Clock(start));Baseline(service,10);
                if(cause!="missing-start")service.Observe(Snapshot(3,canon,Job(9,16)));
                if(cause=="manual")service.Adjust(canon,new(InventoryAction.SetCountedTotal,10,3,Entry(service).Revision,Guid.NewGuid()),start.AddSeconds(3));
                if(cause=="gap")service.Observe(new(start.AddSeconds(30),[],"Test outage"));
                var complete=Snapshot(cause=="gap"?31:4,canon,Job(9,128));
                if(cause=="restart")complete=Snapshot(4,canon,Job(9,128,submitted:-1));
                if(cause=="mapping")complete=complete with {Printers=complete.Printers.Select(p=>p.Queue==canon?p with{Evidence=p.Evidence! with{Usb=p.Evidence!.Usb with{Mapping="AMBIGUOUS_SHARED_DEVICE"}}}:p).ToArray()};
                service.Observe(complete);
                Check(Entry(service).EstimatedSheetsRemaining==10 && Entry(service).ReconciliationRequired,cause+": unsafe attribution preserves count");
            }
            var pendingPath=PathFor();var pendingSource=new Source();pendingSource.Receipts[(canon,11)]=Receipt(11,2,completed:12);
            using(var service=new PaperInventoryService(pendingPath,source:pendingSource,clock:new Clock(start)))
            {Baseline(service,10);service.Observe(Snapshot(3,canon,Job(11,16)));}
            using(var restart=new PaperInventoryService(pendingPath,source:pendingSource,clock:new Clock(start.AddSeconds(10))))
            {
                restart.Observe(Snapshot(11,canon,Job(11,16)));restart.Observe(Snapshot(12,canon,Job(11,128)));
                Check(Entry(restart).EstimatedSheetsRemaining==10 && Entry(restart).LastUsageDecision!.Reason.Contains("continuous"),"Pending job across restart requires recount despite an eventual valid receipt");
            }
            bool fail=false;var retry=new Source();retry.Receipts[(canon,10)]=Receipt(10,7);
            using(var service=new PaperInventoryService(PathFor(),(p,t)=>{if(fail)throw new IOException();File.WriteAllText(p,t);},retry,new Clock(start)))
            {
                Baseline(service,10);service.Observe(Snapshot(3,canon,Job(10,16,pages:7)));fail=true;
                Check(service.Observe(Snapshot(4,canon,Job(10,128,pages:7))).Count==0 && Entry(service).EstimatedSheetsRemaining==10 && Entry(service).AppliedUsageJobKeys.Count==0,"Failed atomic save returns no notice and commits no deduction/ledger");
                fail=false;
                Check(service.Observe(Snapshot(5,canon,Job(10,128,pages:7))).Single().Kind==PaperNotificationKind.Low && Entry(service).EstimatedSheetsRemaining==3,"Receipt retry after save failure applies once");
            }
            var buffer=Marshal.AllocHGlobal(220);
            try
            {
                Marshal.Copy(new byte[220],0,buffer,220);Marshal.WriteInt16(buffer,68,220);Marshal.WriteInt32(buffer,72,0x1100);Marshal.WriteInt16(buffer,86,2);Marshal.WriteInt16(buffer,94,2);
                var layout=PrinterSignals.ReadRequestedLayout(buffer,buffer,220);
                Check(layout.Copies==2 && layout.Duplex=="LONG_EDGE_DUPLEX_REQUESTED","DEVMODE captures requested copies/duplex without inferring consumed sheets");
                Marshal.WriteInt32(buffer,72,0);
                Check(PrinterSignals.ReadRequestedLayout(buffer,buffer,220)==(null,null),"Absent dmFields do not invent defaults");
                Check(PrinterSignals.ReadRequestedLayout(buffer,buffer,80)==(null,null),"Truncated DEVMODE is ignored safely");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            Console.WriteLine($"PASS: {checks} automatic usage safety checks. Positive deductions use a SYNTHETIC attesting adapter; production Windows authority remains disabled.");
        }
        finally { foreach(var file in dir.GetFiles())file.Delete();dir.Delete(); }
    }
}
