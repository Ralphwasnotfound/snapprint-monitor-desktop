using SnapPrintMonitoringDesktop;
internal static class PaperInputChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        Check(PaperInput.Preview(null, InventoryAction.SetCountedTotal, 5).Remaining == 5, "Set amount initializes to 5");
        Check(PaperInput.Preview(5, InventoryAction.AddRefill, 5).Remaining == 10, "Add increases amount");
        Check(PaperInput.Preview(5, InventoryAction.RecordConfirmedUsage, 3).Remaining == 2, "Subtract preview is 2");
        Check(PaperInput.Preview(5, InventoryAction.RecordConfirmedUsage, 4).Remaining == 1, "Example preview is 1");
        Check(PaperInput.Preview(5, InventoryAction.RecordConfirmedUsage, 5).Remaining == 0, "Empty is valid");
        Check(!PaperInput.Preview(5, InventoryAction.RecordConfirmedUsage, 6).Valid, "Negative stock blocked");
        Check(!PaperInput.Preview(null, InventoryAction.AddRefill, 5).Valid, "Unknown baseline cannot add");
        Check(!PaperInput.Preview(null, InventoryAction.RecordConfirmedUsage, 1).Valid, "Unknown baseline cannot subtract");
        Check(!PaperInput.Preview(5, InventoryAction.AddRefill, 0).Valid, "Zero refill blocked");
        Check(!PaperInput.Preview(100000, InventoryAction.AddRefill, 1).Valid, "Overflow blocked");
        Check(!PaperInput.Preview(5, InventoryAction.SetCountedTotal, -1).Valid, "Negative input blocked");
        Check(PaperInput.Preview(5, InventoryAction.SetCountedTotal, 0).Valid, "Set empty allowed");
        var before = PaperInventory.Empty("EPSON L3210 Series") with { EstimatedSheetsRemaining = 5, LowPaperThreshold = 2 };
        var low = PaperAlertPolicy.Plan(before, before with { EstimatedSheetsRemaining = 1 }, InventoryAction.RecordConfirmedUsage);
        Check(low.Notification?.Kind == PaperNotificationKind.Low, "5 minus 4 immediately emits LOW at threshold 2");
        Check(PaperAlertPolicy.Plan(low.Inventory, low.Inventory with { EstimatedSheetsRemaining = 0 }, InventoryAction.RecordConfirmedUsage).Notification?.Kind == PaperNotificationKind.Empty, "1 minus 1 emits EMPTY");
        Console.WriteLine($"PASS: {checks} simple paper input/preview checks.");
    }
}
