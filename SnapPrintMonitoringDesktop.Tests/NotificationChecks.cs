using System.Text.Json;
using System.Text.Json.Serialization;
using SnapPrintMonitoringDesktop;

internal static class NotificationChecks
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("snapprint-notification-tests-");
        try
        {
            int checks=0;
            void Check(bool condition,string message) { if(!condition) throw new Exception(message); checks++; }
            const string canon="Canon MG3000 series", epson="EPSON L3210 Series";
            var now=DateTimeOffset.UtcNow;
            var path=Path.Combine(directory.FullName,"inventory.json");
            using(var service=new PaperInventoryService(path))
            {
                PaperInventory Entry(string name=canon)=>service.Current.Single(i=>i.PrinterName==name);
                PaperNotification? Change(InventoryAction kind,int sheets,string name=canon,int threshold=3)=>
                    service.Adjust(name,new(kind,sheets,threshold,Entry(name).Revision,Guid.NewGuid()),now);
                Check(PaperAlertPolicy.Plan(PaperInventory.Empty(canon),PaperInventory.Empty(canon),InventoryAction.ChangeThreshold).Notification is null,"Uninitialized inventory never alerts");
                Check(Change(InventoryAction.SetCountedTotal,10) is null,"Healthy initialization is silent");
                var low=Change(InventoryAction.RecordConfirmedUsage,7);
                Check(low?.Kind==PaperNotificationKind.Low && Entry().EstimatedSheetsRemaining==3,"10 to 3 emits LOW after saved change");
                Check(low!.Title=="Print and Snap - Low Paper" && low.Body.Contains(canon) && low.Body.Contains("3 estimated sheets") && low.Body.Contains("not verified"),"LOW content clearly says estimated, not physical");
                Check(Change(InventoryAction.RecordConfirmedUsage,1) is null,"3 to 2 does not duplicate LOW");
                var empty=Change(InventoryAction.RecordConfirmedUsage,2);
                Check(empty?.Kind==PaperNotificationKind.Empty && empty.Title=="Print and Snap - Paper Empty" && empty.Body.Contains("0 estimated sheets"),"2 to 0 emits EMPTY");
                Check(Change(InventoryAction.SetCountedTotal,0) is null,"Saving empty again does not repeat");
                Check(Change(InventoryAction.AddRefill,2) is null && Change(InventoryAction.RecordConfirmedUsage,2) is null,"Small refill below threshold does not re-arm LOW or EMPTY");
                Check(Change(InventoryAction.AddRefill,10) is null && !Entry().Alerts!.LowIssued && !Entry().Alerts!.EmptyIssued,"0 to 10 genuine refill resets eligibility");
                Check(Change(InventoryAction.RecordConfirmedUsage,7)?.Kind==PaperNotificationKind.Low,"10 to 3 again emits new LOW");
                Check(Change(InventoryAction.SetCountedTotal,10,epson) is null && Change(InventoryAction.RecordConfirmedUsage,7,epson)?.Body.Contains(epson)==true,"Epson alerts independently of Canon");
                Check(Entry().EstimatedSheetsRemaining==3 && Entry(epson).EstimatedSheetsRemaining==3,"Independent alert state preserves both counts");
                var alerts=Entry().Alerts;
                for(int i=0;i<10;i++) service.Observe(new(now.AddSeconds(i),[],"Test monitoring gap"));
                Check(Entry().Alerts==alerts && Entry().EstimatedSheetsRemaining==3,"3-second refresh/reconciliation changes never re-arm alerts or deduct");
                Check(Change(InventoryAction.ChangeThreshold,0,threshold:0) is null && Change(InventoryAction.ChangeThreshold,0) is null,"Threshold toggles do not reset existing alert history");
                Check(Change(InventoryAction.SetCountedTotal,10) is null && !Entry().Alerts!.LowIssued,"Operator-counted stock increase above threshold re-arms");
                var request=new InventoryAdjustment(InventoryAction.RecordConfirmedUsage,7,3,Entry().Revision,Guid.NewGuid());
                Check(service.Adjust(canon,request,now)?.Kind==PaperNotificationKind.Low && service.Adjust(canon,request,now) is null,"Duplicate saved request does not resend notification");
                var stored=JsonDocument.Parse(File.ReadAllText(path));
                Check(stored.RootElement.GetProperty("Inventories")[0].GetProperty("Alerts").GetProperty("LowIssued").GetBoolean(),"Alert eligibility persists in same atomic inventory write");
                stored.Dispose();
            }
            using(var restarted=new PaperInventoryService(path))
            {
                var entry=restarted.Current.Single(i=>i.PrinterName==canon);
                Check(entry.Alerts!.LowIssued && entry.EstimatedSheetsRemaining==3,"Restart loads existing LOW history without replay");
                Check(restarted.Adjust(canon,new(InventoryAction.RecordConfirmedUsage,1,3,entry.Revision,Guid.NewGuid()),now) is null,"Restart then 3 to 2 remains silent");
                entry=restarted.Current.Single(i=>i.PrinterName==canon);
                Check(restarted.Adjust(canon,new(InventoryAction.RecordConfirmedUsage,2,3,entry.Revision,Guid.NewGuid()),now)?.Kind==PaperNotificationKind.Empty,"Restart still allows a new EMPTY crossing");
            }
            using(var restarted=new PaperInventoryService(path))
            {
                var entry=restarted.Current.Single(i=>i.PrinterName==canon);
                Check(entry.Alerts!.EmptyIssued && restarted.Adjust(canon,new(InventoryAction.SetCountedTotal,0,3,entry.Revision,Guid.NewGuid()),now) is null,"Restart at EMPTY does not spam");
            }
            // Simulate an old v1 inventory file which predates alert metadata.
            var legacyPath=Path.Combine(directory.FullName,"legacy.json");
            File.WriteAllText(legacyPath,JsonSerializer.Serialize(new {Version=1,Inventories=new[]{PaperInventory.Empty(canon) with {EstimatedSheetsRemaining=3,LastInventoryUpdate=now},PaperInventory.Empty(epson)}},new JsonSerializerOptions{DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull}));
            using(var legacy=new PaperInventoryService(legacyPath))
            {
                var entry=legacy.Current[0];
                Check(entry.Alerts!.LowIssued && legacy.Adjust(canon,new(InventoryAction.RecordConfirmedUsage,1,3,entry.Revision,Guid.NewGuid()),now) is null,"Old low inventory seeds history silently during migration");
            }
            bool fail=false;
            using(var service=new PaperInventoryService(Path.Combine(directory.FullName,"failure.json"),(p,t)=>{if(fail)throw new IOException();File.WriteAllText(p,t);}))
            {
                service.Adjust(canon,new(InventoryAction.SetCountedTotal,10,3,0,Guid.NewGuid()),now);
                fail=true; bool rejected=false;
                try { service.Adjust(canon,new(InventoryAction.RecordConfirmedUsage,7,3,service.Current[0].Revision,Guid.NewGuid()),now); } catch(InvalidOperationException){rejected=true;}
                Check(rejected && service.Current[0].EstimatedSheetsRemaining==10 && !service.Current[0].Alerts!.LowIssued,"Failed save emits no alert and consumes no eligibility");
                fail=false;
                Check(service.Adjust(canon,new(InventoryAction.RecordConfirmedUsage,7,3,service.Current[0].Revision,Guid.NewGuid()),now)?.Kind==PaperNotificationKind.Low,"Successful retry after failed save notifies");
            }
            var queuedEntry = PaperInventory.Empty(canon) with { EstimatedSheetsRemaining = 3 };
            var queuedLow = PaperAlertPolicy.Create(queuedEntry,PaperNotificationKind.Low);
            Check(PaperAlertPolicy.RefreshPending(queuedLow,[queuedEntry with {EstimatedSheetsRemaining=10}]) is null,"Refill cancels a queued stale LOW before Windows submission");
            Check(PaperAlertPolicy.RefreshPending(queuedLow,[queuedEntry with {EstimatedSheetsRemaining=2}])!.Body.Contains("2 estimated sheets"),"Queued LOW uses latest estimate without generating another crossing");
            Check(PaperAlertPolicy.RefreshPending(queuedLow,[queuedEntry with {EstimatedSheetsRemaining=0}]) is null,"Queued LOW is superseded by EMPTY");
            Check(PaperAlertPolicy.RefreshPending(PaperNotification.Test,[])==PaperNotification.Test,"Test notification does not depend on an inventory entry");
            Check(PaperNotification.Test.Kind==PaperNotificationKind.Test && PaperNotification.Test.Body.Contains("No printer, inventory or alert eligibility was changed"),"Notification test is explicitly independent of inventory");
            Console.WriteLine($"PASS: {checks} notification policy/persistence checks (no Windows balloons sent by this suite).");
        }
        finally { foreach(var file in directory.GetFiles()) file.Delete(); directory.Delete(); }
    }
}
