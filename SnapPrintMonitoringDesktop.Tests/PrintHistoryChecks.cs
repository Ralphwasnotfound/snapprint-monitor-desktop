using SnapPrintMonitoringDesktop;
internal static class PrintHistoryChecks
{
    internal static void Run()
    {
        var dir=Directory.CreateTempSubdirectory("snapprint-history-tests-");
        int checks=0; void Check(bool value,string message) { if(!value) throw new Exception(message);checks++; }
        const string canon="Canon MG3000 series",epson="EPSON L3210 Series";
        var now=DateTimeOffset.UtcNow;
        PrinterSnapshot Sample(int time, string name, params JobReading[] jobs)
        {
            var evidence=PrinterSignals.Classify(new("EXACT_QUEUE_DEVICE_ID",@"USBPRINT\TEST\1",true,8,0,"fixture"),new Dictionary<string,string>(),jobs,"AVAILABLE","NORMAL_REPORTED");
            return new(now.AddSeconds(time),[new PrinterReading(name,"UNKNOWN","fixture") { Evidence=evidence },new PrinterReading(name==canon?epson:canon,"UNKNOWN","fixture") { Evidence=evidence with { Jobs=[] } }],null);
        }
        var path=Path.Combine(dir.FullName,"history.json");
        JobReading Job(uint id,uint status,uint pages,uint printed)=>new(id,status,pages,printed) { SubmittedAt=now.AddSeconds(id), CopiesRequested=2,DuplexRequested="LONG_EDGE_DUPLEX_REQUESTED", RequestedSettings=new(220,0,2,9,null,null,null,null) };
        try
        {
            using(var history=new PrintHistoryService(path))
            {
                history.Observe(Sample(1,canon,Job(1,16,1,0)));
                Check(history.Current.Single().Active && history.Current.Single().Status.Contains("progress unavailable"),"Active without invented progress");
                history.Observe(Sample(2,canon,Job(1,16,1,1)));
                Check(history.Current.Count==1 && history.Current[0].PagesReported==1,"Repeated poll updates one job");
                history.Observe(Sample(3,canon,Job(1,128,1,1)));
                Check(!history.Current[0].PhysicalCompletionVerified && history.Current[0].EstimatedSheetsUsed is null && !history.Current[0].Active,"Spooler completion never physical success");
                history.Observe(Sample(4,epson,Job(2,16,6,3)));
                Check(history.Current.Count==2 && history.Current.Single(e=>e.Printer==epson).PagesReported==3,"Printer isolation and multi-page counters");
                Check(history.Current[0].CopiesRequested==2 && history.Current[0].DuplexRequested=="LONG_EDGE_DUPLEX_REQUESTED" && history.Current[0].EstimatedSheetsUsed is null,"Copies/duplex retained without sheet arithmetic");
                history.Observe(Sample(5,epson,Job(2,4,6,3)));
                Check(history.Current.Single(e=>e.JobId==2).Status.Contains("Cancelled"),"Cancellation recorded");
                history.Observe(Sample(6,epson));
                Check(history.Current.Single(e=>e.JobId==2).Status.Contains("Cancelled"),"Cancellation evidence survives disappearance");
                history.Observe(Sample(7,canon,Job(3,2,0,0)));
                Check(history.Current.First().Status.Contains("Error") && history.Current.First().PagesText=="Unknown","Error and unknown pages");
                history.Observe(Sample(30,canon));
                Check(history.Current.First().MonitoringGap && history.Current.First().Status.Contains("Unknown outcome"),"Gap/disappearance cannot be success");
                history.Observe(Sample(31,canon,Job(4,16,2,0)));
            }
            using(var history=new PrintHistoryService(path))
            {
                Check(history.Current.First().RequestedSettings?.PaperSizeCode==9,"Requested settings persist through restart");
                Check(history.Current.Count==4 && history.Current.All(e=>!e.Active),"Restart persistence closes active observations conservatively");
                Check(history.Current.Single(e=>e.JobId==4).MonitoringGap,"Restart marks monitoring interruption");
                history.Observe(Sample(32,canon,Job(4,128,2,2)));
                Check(history.Current.Count==4,"Timestamped identity deduplicates across restart");
                Check(!File.ReadAllText(path).Contains("TestDocument"),"No private document contents/names collected");
                var opted=Sample(33,canon,Job(1,128,4,4) with { SubmittedAt=now.AddSeconds(32), CopiesRequested=1, DuplexRequested="SIMPLEX_REQUESTED" }) with { PaperInventories=[PaperInventory.Empty(canon) with { AutomaticPaperEstimateRequested=true }] };
                history.Observe(opted);
                Check(history.Current.Count==5,"Reused job ID with new submission is a different job");
                Check(history.Current.First().AutomaticEstimateRequested && history.Current.First().AccountingDecision?.Contains("semantics")==true,"Opt-in accounting decision persisted with observed metadata");
                history.Observe(Sample(34,canon,Enumerable.Range(10,520).Select(i=>Job((uint)i,128,1,1)).ToArray()));
                Check(history.Current.Count==PrintHistoryService.Limit,"Retention bounded to 500 jobs");
                history.Observe(new(now.AddDays(31),[],null));
                Check(history.Current.Count==0,"30-day retention prunes old observations");
            }
            Console.WriteLine($"PASS: {checks} synthetic print-history checks.");
        }
        finally { foreach(var file in dir.GetFiles()) file.Delete();dir.Delete(); }
    }
}
