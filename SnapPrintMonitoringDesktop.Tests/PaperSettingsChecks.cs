using SnapPrintMonitoringDesktop;
internal static class PaperSettingsChecks
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("snapprint-paper-settings-");
        int checks=0;
        void Check(bool value,string reason) { if(!value) throw new Exception(reason); checks++; }
        var path=Path.Combine(directory.FullName,"settings.json");
        const string canon="Canon MG3000 series", epson="EPSON L3210 Series";
        try
        {
            using(var service=new PaperInventoryService(path))
            {
                PaperInventory Entry(string name)=>service.Current.Single(i=>i.PrinterName==name);
                PaperNotification? Save(string name,int amount,int threshold)=>service.Adjust(name,new(InventoryAction.SetCountedTotal,amount,threshold,Entry(name).Revision,Guid.NewGuid()),DateTimeOffset.UtcNow);
                Check(Save(canon,10,5) is null && Entry(canon).EstimatedSheetsRemaining==10 && Entry(canon).LowPaperThreshold==5,"Two settings saved together");
                Check(!Entry(canon).AutomaticPaperEstimateRequested,"Automatic estimate defaults off");
                service.Adjust(canon,new(InventoryAction.SetCountedTotal,10,5,Entry(canon).Revision,Guid.NewGuid(),true),DateTimeOffset.UtcNow);
                Check(Entry(canon).AutomaticPaperEstimateRequested && !Entry(canon).AutomaticDeductionEnabled,"Opt-in is permission, not evidence");
                Save(epson,20,2);
                Check(!Entry(epson).AutomaticPaperEstimateRequested,"Opt-in is printer-specific");
                Check(Save(canon,5,5)?.Kind==PaperNotificationKind.Low,"Threshold itself emits LOW");
                Check(Save(canon,4,5) is null,"Repeated low save suppressed");
                Check(Save(canon,0,5)?.Kind==PaperNotificationKind.Empty,"Zero save emits EMPTY");
                Save(canon,10,5);
                Check(Save(canon,5,5)?.Kind==PaperNotificationKind.Low,"Higher loaded amount rearms alerts");
                Check(Entry(epson).EstimatedSheetsRemaining==20 && Entry(epson).LowPaperThreshold==2,"Independent printer settings preserved");
                Check(!Entry(canon).AutomaticDeductionEnabled && !Entry(canon).PhysicalCountVerified,"Settings do not enable physical counting");
            }
            using(var service=new PaperInventoryService(path))
            {
                var entry=service.Current.Single(i=>i.PrinterName==canon);
                Check(entry.SheetsLoaded==5 && entry.TotalEstimatedSheetsAccountedFor==0,"Summary settings persist without unknown job deductions");
                Check(entry.AutomaticPaperEstimateRequested,"Opt-in persists across restart");
                Check(entry.EstimatedSheetsRemaining==5 && entry.LowPaperThreshold==5,"Both settings persist across restart");
                Check(service.Current.Single(i=>i.PrinterName==epson).EstimatedSheetsRemaining==20,"Epson persists independently");
                Check(service.Adjust(canon,new(InventoryAction.SetCountedTotal,5,5,entry.Revision,Guid.NewGuid()),DateTimeOffset.UtcNow) is null,"Restart does not repeat LOW");
                entry=service.Current.Single(i=>i.PrinterName==canon);
                service.Adjust(canon,new(InventoryAction.SetCountedTotal,5,5,entry.Revision,Guid.NewGuid(),false),DateTimeOffset.UtcNow);
                Check(!service.Current.Single(i=>i.PrinterName==canon).AutomaticPaperEstimateRequested,"Opt-out saved without resetting count");
            }
        }
        finally { foreach(var file in directory.GetFiles()) file.Delete(); directory.Delete(); }
        Console.WriteLine($"PASS: {checks} two-field paper settings checks.");
    }
}
