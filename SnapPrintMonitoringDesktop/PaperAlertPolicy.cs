namespace SnapPrintMonitoringDesktop;

internal sealed record PaperAlertState(bool LowIssued, bool EmptyIssued);
internal enum PaperNotificationKind { Low, Empty, Test }
internal sealed record PaperNotification(PaperNotificationKind Kind, string Title, string Body, string? PrinterName = null)
{
    internal static PaperNotification Test => new(PaperNotificationKind.Test, "Print and Snap - Notification Test",
        "TEST ONLY: Windows paper notifications are available for testing. No printer, inventory or alert eligibility was changed.");
}
internal static class PaperAlertPolicy
{
    // Loading existing stock is not a threshold crossing; seed old files silently.
    internal static PaperAlertState Bootstrap(PaperInventory inventory) => inventory.Alerts ??
        new(inventory.LowPaperWarning || inventory.EmptyWarning, inventory.EmptyWarning);

    internal static PaperNotification Create(PaperInventory inventory, PaperNotificationKind kind)
    {
        const string qualifier = " Sensorless estimate; physical paper level is not verified.";
        return kind == PaperNotificationKind.Empty
            ? new(kind, "Print and Snap - Paper Empty", $"{inventory.PrinterName} has 0 estimated sheets remaining. Please refill and recount." + qualifier, inventory.PrinterName)
            : new(kind, "Print and Snap - Low Paper", $"{inventory.PrinterName} has only {inventory.EstimatedSheetsRemaining} estimated sheets remaining. Please refill soon." + qualifier, inventory.PrinterName);
    }
    internal static PaperNotification? RefreshPending(PaperNotification notification, IReadOnlyList<PaperInventory> inventories)
    {
        if (notification.Kind == PaperNotificationKind.Test) return notification;
        var current = inventories.FirstOrDefault(i => string.Equals(i.PrinterName, notification.PrinterName, StringComparison.OrdinalIgnoreCase));
        if (current is null) return null;
        if (notification.Kind == PaperNotificationKind.Low && current.LowPaperWarning || notification.Kind == PaperNotificationKind.Empty && current.EmptyWarning)
            return Create(current, notification.Kind);
        return null; // Refill/changed warning state cancels a balloon that has not yet been submitted to Windows.
    }
    internal static (PaperInventory Inventory, PaperNotification? Notification) Plan(
        PaperInventory previous, PaperInventory next, InventoryAction action)
    {
        var state = Bootstrap(previous);
        PaperNotification? notification = null;
        if (next.EstimatedSheetsRemaining is int sheets)
        {
            // An operator-reported stock increase is required: threshold edits alone never re-arm.
            bool refill = action is InventoryAction.AddRefill or InventoryAction.SetCountedTotal &&
                previous.EstimatedSheetsRemaining is int old && sheets > old && sheets > next.LowPaperThreshold;
            if (refill) state = new(false, false);

            if (next.EmptyWarning && !state.EmptyIssued)
            {
                state = new(true, true); // EMPTY also suppresses a later LOW after a small, insufficient refill.
                notification = Create(next, PaperNotificationKind.Empty);
            }
            else if (next.LowPaperWarning && !state.LowIssued)
            {
                state = state with { LowIssued = true };
                notification = Create(next, PaperNotificationKind.Low);
            }
        }
        return (next with { Alerts = state }, notification);
    }
}
