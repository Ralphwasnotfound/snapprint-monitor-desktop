using SnapPrintMonitoringDesktop;
internal static class SpoolerEstimateChecks
{
    internal static void Run()
    {
        int checks=0;void Check(bool value,string reason) { if(!value) throw new Exception(reason);checks++; }
        string Decide(bool opt=true,uint flags=128,uint pages=4,uint printed=4,int? copies=1,string? duplex="SIMPLEX_REQUESTED",bool gap=false,bool identity=true)=>SpoolerEstimatePolicy.Decision(opt,flags,pages,printed,copies,duplex,gap,identity);
        Check(Decide(opt:false).Contains("off"),"Opt-out never deducts");
        foreach(var pair in new[]{(1u,1,"SIMPLEX_REQUESTED"),(4u,1,"SIMPLEX_REQUESTED"),(4u,1,"LONG_EDGE_DUPLEX_REQUESTED"),(4u,2,"SIMPLEX_REQUESTED")})
            Check(Decide(pages:pair.Item1,printed:pair.Item1,copies:pair.Item2,duplex:pair.Item3).Contains("semantics"),"Page/copy/layout semantics must be established even when requested values exist");
        Check(Decide(copies:null).Contains("Missing print settings"),"Unknown copies");
        Check(Decide(duplex:null).Contains("Missing print settings"),"Unknown duplex");
        Check(Decide(pages:0,printed:0).Contains("Missing reported"),"Missing pages");
        Check(Decide(flags:4).Contains("Cancelled"),"Cancellation excluded");
        Check(Decide(flags:2).Contains("failed"),"Error excluded");
        Check(Decide(flags:16).Contains("Unknown job outcome"),"Printing not completion");
        Check(Decide(gap:true).Contains("continuity"),"Monitoring gap excluded");
        Check(Decide(identity:false).Contains("identity"),"Missing timestamp excluded");
        Check(Decide(printed:2).Contains("Partial"),"Partial counts excluded");
        var entry=new PrintHistoryEntry("fixture","EPSON L3210 Series",1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,4,4,1,"SIMPLEX_REQUESTED",128,"Spooler completion reported",false,false) { AutomaticEstimateRequested=true };
        Check(entry.Accounting.Contains("semantics") && !entry.InventoryDeducted && entry.EstimatedSheetsUsed is null && !entry.PhysicalCompletionVerified,"History transparently rejects uncertain estimates");
        Console.WriteLine($"PASS: {checks} opt-in spooler evidence checks; no physical sheet inference.");
    }
}
