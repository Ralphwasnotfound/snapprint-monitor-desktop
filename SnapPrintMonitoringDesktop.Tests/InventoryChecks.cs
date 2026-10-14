using SnapPrintMonitoringDesktop;

internal static class InventoryChecks
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("snapprint-inventory-tests-");
        try
        {
            int checks = 0;
            void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
            var now = DateTimeOffset.UtcNow;
            const string canon = "Canon MG3000 series", epson = "EPSON L3210 Series";
            int sampleTime = 0;
            PrinterSnapshot Sample(int seconds, params JobReading[] jobs)
            {
                sampleTime = seconds;
                var evidence = PrinterSignals.Classify(new("EXACT_QUEUE_DEVICE_ID", @"USBPRINT\FIXTURE\1", true, 8, 0, "Fixture"),
                    new Dictionary<string,string> { ["PrintQueue.NumberOfJobs"] = jobs.Length.ToString() }, jobs, "AVAILABLE", jobs.Length > 0 ? "PRINTING" : "NORMAL_REPORTED");
                return new(now.AddSeconds(seconds), [new PrinterReading(canon,"UNKNOWN","fixture") { Evidence = evidence },
                    new PrinterReading(epson,"UNKNOWN","fixture") { Evidence = evidence with { Jobs = [], QueueStatus = "NORMAL_REPORTED", Signals = new Dictionary<string,string> { ["PrintQueue.NumberOfJobs"] = "0" } } }], null);
            }
            var path = Path.Combine(directory.FullName,"inventory.json");
            using (var service = new PaperInventoryService(path))
            {
                PaperInventory Entry(string name=canon) => service.Current.Single(i=>i.PrinterName==name);
                void Adjust(InventoryAction kind, int sheets, int threshold=3, Guid? id=null, long? revision=null) =>
                    service.Adjust(canon, new(kind,sheets,threshold,revision ?? Entry().Revision,id ?? Guid.NewGuid()),now.AddSeconds(sampleTime + 1));
                bool Reject(Action action) { try { action(); return false; } catch(InvalidOperationException) { return true; } }
                Check(Entry().EstimatedSheetsRemaining is null && Entry().LowPaperThreshold==3 && !Entry().EmptyWarning, "Unknown initial count is not empty");
                service.Observe(Sample(0)); Adjust(InventoryAction.SetCountedTotal, 20);
                Check(Entry().EstimatedSheetsRemaining==20 && Entry().SynchronizationStatus=="OPERATOR_BASELINE" && !Entry().PhysicalCountVerified, "Operator baseline, not verified count");
                Check(Entry(epson).EstimatedSheetsRemaining is null, "Printers have independent inventories");
                var id=Guid.NewGuid(); var revision=Entry().Revision;
                Adjust(InventoryAction.AddRefill,5,id:id); Adjust(InventoryAction.AddRefill,5,id:id,revision:revision);
                Check(Entry().EstimatedSheetsRemaining==25, "Repeated refill request is idempotent");
                Check(Reject(()=>Adjust(InventoryAction.AddRefill,1,revision:revision)), "Stale dialog cannot overwrite current inventory");
                Adjust(InventoryAction.RecordConfirmedUsage,22);
                Check(Entry().EstimatedSheetsRemaining==3 && Entry().LowPaperWarning && !Entry().EmptyWarning, "Default low-paper boundary");
                var timestamp=Entry().LastInventoryUpdate;
                Adjust(InventoryAction.ChangeThreshold,0,2);
                Check(!Entry().LowPaperWarning && Entry().LastInventoryUpdate==timestamp, "Configurable threshold does not fake count timestamp");
                Adjust(InventoryAction.RecordConfirmedUsage,3);
                Check(Entry().EmptyWarning && !Entry().LowPaperWarning, "Empty warning separate from low");
                Check(Reject(()=>Adjust(InventoryAction.RecordConfirmedUsage,1)), "Negative counts rejected, not silently clamped");
                Check(Reject(()=>Adjust(InventoryAction.SetCountedTotal,-1)), "Negative operator input rejected");
                Check(Reject(()=>Adjust(InventoryAction.ChangeThreshold,0,-1)), "Invalid threshold rejected");
                Adjust(InventoryAction.SetCountedTotal,30);
                service.Adjust(epson,new(InventoryAction.SetCountedTotal,50,8,Entry(epson).Revision,Guid.NewGuid()),now.AddSeconds(sampleTime+1));
                service.Observe(Sample(3,new JobReading(7,16,10,0)));
                var observed=Entry();
                for(int i=0;i<10;i++) service.Observe(Sample(4,new JobReading(7,16,10,0)));
                Check(Entry().EstimatedSheetsRemaining==30 && Entry().Revision==observed.Revision && Entry().LastInventoryUpdate==observed.LastInventoryUpdate, "Repeated polling never deducts or repeatedly modifies inventory");
                Check(Entry().ReconciliationRequired, "Uncertain print activity flags reconciliation");
                Check(Entry(epson).EstimatedSheetsRemaining==50 && !Entry(epson).ReconciliationRequired, "Canon print activity does not affect Epson inventory");
                Adjust(InventoryAction.SetCountedTotal,30);
                Check(Entry().ReconciliationRequired, "Count during active job cannot claim reconciliation");
                service.Observe(Sample(5,new JobReading(7,128|4096,10,10)));
                Check(Entry().EstimatedSheetsRemaining==30 && Entry().ReconciliationRequired, "Completed job and PagesPrinted do not prove sheets used");
                service.Observe(Sample(6));
                Check(Entry().ReconciliationRequired, "Job disappearance never silently clears uncertainty");
                Adjust(InventoryAction.AddRefill,10);
                Check(Entry().EstimatedSheetsRemaining==40 && Entry().ReconciliationRequired, "Refill preserves pending reconciliation");
                Adjust(InventoryAction.RecordConfirmedUsage,2);
                Check(Entry().EstimatedSheetsRemaining==38 && Entry().ReconciliationRequired, "Confirmed usage changes estimate without resolving other unknown consumption");
                Adjust(InventoryAction.SetCountedTotal,38);
                service.Observe(Sample(50));
                Check(Entry().ReconciliationRequired, "Long monitoring gap requires reconciliation");
                Adjust(InventoryAction.SetCountedTotal,38);
                service.Observe(new(now.AddSeconds(51),[],"Spooler unavailable"));
                Check(Entry().ReconciliationRequired, "Discovery failure cannot leave baseline trusted");
                service.Observe(Sample(52)); Adjust(InventoryAction.SetCountedTotal,38);
                var full=service.Attach(Sample(53));
                Check(full.PaperInventories.Count==2 && full.Printers[0].PaperInventory?.EstimatedSheetsRemaining==38, "API attachment includes per-printer and root inventories");
                Check(service.Attach(new(now,[],null)).PaperInventories.Count==2, "Missing Windows queues retain inventories");
                Check(!Entry().AutomaticDeductionEnabled, "No automatic deductions without physical evidence");
                // Copies/ranges/duplex are not carried by JOB_INFO_1; no invented sheet arithmetic.
                foreach(var job in new[]{new JobReading(8,128,6,6),new JobReading(9,4096,3,3),new JobReading(10,2,0,1)})
                    service.Observe(Sample(54,job));
                Check(Entry().EstimatedSheetsRemaining==38, "Multipage/copies/duplex/ranges/cancellation ambiguity never treated as confirmed sheets");
                using var second=new PaperInventoryService(path);
                Check(second.Error is not null, "Second writer fails safely without overwriting inventory");
            }
            using(var restarted=new PaperInventoryService(path))
            {
                var entry=restarted.Current.Single(i=>i.PrinterName==canon);
                Check(entry.EstimatedSheetsRemaining==38 && entry.LowPaperThreshold==3 && entry.LastInventoryUpdate is not null, "Counts/threshold/timestamp persist across restart");
                Check(entry.ReconciliationRequired && entry.ReconciliationReason.Contains("restart"), "Restart exposes unmonitored downtime");
            }
            var corrupt=Path.Combine(directory.FullName,"corrupt.json"); File.WriteAllText(corrupt,"not json");
            using(var service=new PaperInventoryService(corrupt))
                Check(service.Error is not null && service.Current.All(i=>i.EstimatedSheetsRemaining is null) && File.ReadAllText(corrupt)=="not json", "Corrupt file preserved; never reset to a trusted zero");
            var writes=0;
            using(var service=new PaperInventoryService(Path.Combine(directory.FullName,"failure.json"),(p,t)=>{ writes++; throw new IOException(); }))
            {
                bool failed=false;
                try {service.Adjust(canon,new(InventoryAction.SetCountedTotal,10,3,0,Guid.NewGuid()),now);} catch(InvalidOperationException){failed=true;}
                Check(failed && service.Error is not null && service.Current[0].EstimatedSheetsRemaining is null && writes==1, "Failed persistence never acknowledges an unsaved adjustment");
            }
            var recoveryFail = false;
            using(var service=new PaperInventoryService(Path.Combine(directory.FullName,"recover.json"),(p,t)=>
                { if(recoveryFail) throw new IOException(); File.WriteAllText(p,t); }))
            {
                service.Observe(Sample(0));
                service.Adjust(canon,new(InventoryAction.SetCountedTotal,10,4,service.Current[0].Revision,Guid.NewGuid()),now.AddSeconds(1));
                recoveryFail = true;
                try { service.Adjust(canon,new(InventoryAction.AddRefill,3,4,service.Current[0].Revision,Guid.NewGuid()),now.AddSeconds(2)); } catch(InvalidOperationException) { }
                recoveryFail = false;
                service.Adjust(canon,new(InventoryAction.AddRefill,3,4,service.Current[0].Revision,Guid.NewGuid()),now.AddSeconds(3));
                Check(service.Current[0].EstimatedSheetsRemaining==13 && service.Current[0].ReconciliationRequired, "Recovering storage preserves uncertainty and applies only the successful refill");
                service.Adjust(canon,new(InventoryAction.SetCountedTotal,13,4,service.Current[0].Revision,Guid.NewGuid()),now.AddMinutes(1));
                Check(service.Current[0].ReconciliationRequired, "Stale idle observation cannot authorize reconciliation");
                service.Observe(Sample(100));
                service.Adjust(canon,new(InventoryAction.SetCountedTotal,13,4,service.Current[0].Revision,Guid.NewGuid()),now.AddSeconds(101));
                Check(!service.Current[0].ReconciliationRequired, "Fresh idle monitoring and recount establish operator baseline");
                service.Observe(Sample(99));
                Check(service.Current[0].ReconciliationRequired, "Backwards clock change exposes a monitoring gap");
            }
            var unknownPath=Path.Combine(directory.FullName,"unknown.json");
            using(var service=new PaperInventoryService(unknownPath))
            {
                bool rejected=false;
                try {service.Adjust(canon,new(InventoryAction.AddRefill,5,3,0,Guid.NewGuid()),now);} catch(InvalidOperationException){rejected=true;}
                Check(rejected && service.Current[0].EstimatedSheetsRemaining is null, "Refill cannot assume an unknown starting count is zero");
                service.Adjust(canon,new(InventoryAction.ChangeThreshold,0,0,0,Guid.NewGuid()),now);
                Check(service.Current[0].EstimatedSheetsRemaining is null && !service.Current[0].EmptyWarning && service.Current[0].LastInventoryUpdate is null, "Threshold-only configuration does not invent initial stock");
            }
            Console.WriteLine($"PASS: {checks} inventory checks (synthetic jobs; temporary local files only).");
        }
        finally
        {
            foreach(var file in directory.GetFiles()) file.Delete();
            directory.Delete();
        }
    }
}
