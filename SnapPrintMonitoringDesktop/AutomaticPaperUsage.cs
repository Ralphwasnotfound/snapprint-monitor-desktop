using System.Security.Cryptography;
using System.Text;

namespace SnapPrintMonitoringDesktop;

// Only an in-process, independently validated device/driver adapter may implement this contract.
// No HTTP/file/config flag can turn Windows page counters into trusted receipts.
internal interface ICompletedSheetUsageSource
{
    string SourceId { get; }
    bool Supports(string printerName);
    CompletedSheetReceipt? Read(string printerName, string deviceId, JobReading job);
}
internal sealed class WindowsSpoolerSheetSource : ICompletedSheetUsageSource
{
    internal static readonly WindowsSpoolerSheetSource Instance = new();
    public string SourceId => "WindowsSpooler.NoPhysicalSheetAuthority";
    public bool Supports(string printerName) => false;
    public CompletedSheetReceipt? Read(string printerName, string deviceId, JobReading job) => null;
}
internal enum SheetCompletionOutcome { Completed, Partial, Cancelled, Failed }
internal sealed record CompletedSheetReceipt(string ReceiptId, string PrinterName, string DeviceId, uint JobId,
    DateTimeOffset SubmittedAt, DateTimeOffset CompletedAt, int PhysicalSheetsConsumed, SheetCompletionOutcome Outcome);
internal sealed record AutomaticDeduction(DateTimeOffset At, string Source, uint JobId, int Sheets, string ReceiptKey, DateTimeOffset? SubmittedAt = null);
internal sealed record UsageDecision(DateTimeOffset At, uint JobId, string Outcome, string Reason);
internal sealed record UsageResult(PaperInventory Inventory, IReadOnlyList<PaperNotification> Notifications);

internal sealed class AutomaticPaperUsage(ICompletedSheetUsageSource source, DateTimeOffset sessionStarted)
{
    private sealed class Tracked(Guid generation, bool baseline, string device, bool interrupted)
    {
        internal Guid Generation = generation;
        internal bool Baseline = baseline;
        internal string Device = device;
        internal bool Interrupted = interrupted;
        internal bool FaultSeen;
        internal string? ReceiptKey;
        internal string? UsageJobKey;
    }
    private readonly Dictionary<string, Dictionary<string, Tracked>> tracking = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> safeGenerations = new(StringComparer.OrdinalIgnoreCase);
    internal bool Supported(string name) { try { return source.Supports(name); } catch { return false; } }
    internal const int ReceiptLimit = 4096;
    private const uint UnsafeJobFlags = 2 | 4 | 32 | 64 | 256 | 512 | 1024 | 2048;
    private static string Key(JobReading job) => $"{job.Id}:{job.SubmittedAt?.UtcTicks.ToString() ?? "unknown"}";
    internal UsageResult Process(PaperInventory inventory, PrinterReading? reading, DateTimeOffset at, bool interruption)
    {
        var notices = new List<PaperNotification>();
        if (!tracking.TryGetValue(inventory.PrinterName, out var jobs)) tracking[inventory.PrinterName] = jobs = new();
        if (inventory.SynchronizationStatus == "OPERATOR_BASELINE") safeGenerations[inventory.PrinterName] = inventory.ManualCountGeneration;
        if (interruption) { safeGenerations.Remove(inventory.PrinterName); foreach(var item in jobs.Values) item.Interrupted = true; }
        var evidence = reading?.Evidence;
        if (evidence is null || !evidence.JobQueryStatus.StartsWith("AVAILABLE", StringComparison.Ordinal))
            return new(Decide(inventory, at, 0, "REJECTED", "Job monitoring unavailable; no automatic deduction."), notices);
        var current = evidence.Jobs.Select(Key).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in jobs.Keys.Where(k=>!current.Contains(k)).ToArray())
        {
            var previous = jobs[missing];
            if (previous.UsageJobKey is null || !inventory.AppliedUsageJobKeys.Contains(previous.UsageJobKey))
                inventory = Decide(inventory, at, uint.Parse(missing.Split(':')[0]), "REJECTED", "Job disappeared without an accepted completed-sheet receipt; disappearance is not successful physical output.");
            jobs.Remove(missing);
        }
        foreach(var job in evidence.Jobs)
        {
            var key = Key(job);
            var device = evidence.Usb.DeviceInstanceId ?? "";
            bool exactDevice = evidence.Usb.Mapping == "EXACT_QUEUE_DEVICE_ID" && device.StartsWith(@"USBPRINT\", StringComparison.OrdinalIgnoreCase);
            bool active = (job.RawStatus & (8u|16u)) != 0 && !job.CompletionReported;
            if (!jobs.TryGetValue(key, out var tracked))
            {
                // Bound memory; refusing more jobs is safer than dropping identity history.
                if(jobs.Count >= 256) { inventory = Decide(inventory, at, job.Id, "REJECTED", "Too many active job identities; reconcile inventory."); continue; }
                tracked = new(inventory.ManualCountGeneration, safeGenerations.GetValueOrDefault(inventory.PrinterName) == inventory.ManualCountGeneration && inventory.ManualCountGeneration != Guid.Empty && active,
                    device, interruption || job.SubmittedAt is null || job.SubmittedAt < sessionStarted || job.SubmittedAt > at);
                jobs[key] = tracked;
            }
            tracked.FaultSeen |= (job.RawStatus & UnsafeJobFlags) != 0;
            if (tracked.FaultSeen)
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Cancelled, failed, restarted or partially uncertain job; page counters are not physical sheet consumption."); continue; }
            if (!job.CompletionReported)
            { inventory = Decide(inventory, at, job.Id, "PENDING", "Job is not completed; requested pages/copies/duplex do not authorize deduction."); continue; }
            if (!Supported(inventory.PrinterName))
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Windows reports spooler completion only. No validated physical-sheet completion source exists; copies, page ranges and layout cannot establish actual output."); continue; }
            CompletedSheetReceipt? receipt;
            try { receipt = source.Read(inventory.PrinterName, device, job); }
            catch { inventory = Decide(inventory, at, job.Id, "REJECTED", "Validated completion adapter query failed; no deduction."); continue; }
            if (receipt is null)
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "No authoritative completed-sheet receipt; no deduction."); continue; }
            if (string.IsNullOrWhiteSpace(receipt.PrinterName) || string.IsNullOrWhiteSpace(receipt.DeviceId) || string.IsNullOrWhiteSpace(receipt.ReceiptId))
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Receipt identity is missing; no deduction."); continue; }
            var receiptKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceId + "\n" + receipt.PrinterName.ToUpperInvariant() + "\n" + receipt.DeviceId.ToUpperInvariant() + "\n" + receipt.ReceiptId)));
            var usageJobKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceId + "\\n" + inventory.PrinterName.ToUpperInvariant() + "\\n" + device.ToUpperInvariant() + "\\n" + key)));
            tracked.UsageJobKey = usageJobKey;
            tracked.ReceiptKey = receiptKey;
            if (!exactDevice || !string.Equals(tracked.Device, device, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.DeviceId, device, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.PrinterName, inventory.PrinterName, StringComparison.OrdinalIgnoreCase) || receipt.JobId != job.Id || receipt.SubmittedAt != job.SubmittedAt ||
                string.IsNullOrWhiteSpace(receipt.ReceiptId) || receipt.ReceiptId.Length > 256 || receipt.Outcome != SheetCompletionOutcome.Completed ||
                receipt.CompletedAt < receipt.SubmittedAt || receipt.CompletedAt > at || receipt.PhysicalSheetsConsumed is < 1 or > 100000)
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Receipt has incomplete/invalid identity, outcome, timing, mapping or physical sheet count."); continue; }
            if (inventory.AppliedUsageReceiptKeys.Contains(receiptKey) || inventory.AppliedUsageJobKeys.Contains(usageJobKey))
            { inventory = Decide(inventory, at, job.Id, "DUPLICATE", "This receipt was already persisted; no second deduction or notification."); continue; }
            if (!tracked.Baseline || tracked.Interrupted || tracked.Generation == Guid.Empty || tracked.Generation != inventory.ManualCountGeneration || safeGenerations.GetValueOrDefault(inventory.PrinterName) != tracked.Generation)
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Job lacks a continuous observed start after an operator baseline, or manual inventory changed mid-job. Reconcile instead of risking double accounting."); continue; }
            if (inventory.AppliedUsageReceiptKeys.Count >= ReceiptLimit)
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Receipt ledger is full; automatic deduction is stopped rather than pruning duplicate protection."); continue; }
            if (inventory.EstimatedSheetsRemaining is not int stock || receipt.PhysicalSheetsConsumed > stock)
            { inventory = Decide(inventory, at, job.Id, "REJECTED", "Receipt exceeds known inventory or baseline is unknown; count preserved, never clamped to a misleading zero."); continue; }
            var next = inventory with { EstimatedSheetsRemaining = stock - receipt.PhysicalSheetsConsumed, LastInventoryUpdate = at,
                TotalEstimatedSheetsAccountedFor = inventory.TotalEstimatedSheetsAccountedFor is { } accounted ? checked(accounted + receipt.PhysicalSheetsConsumed) : null,
                LastAutomaticDeduction = new(at, source.SourceId, job.Id, receipt.PhysicalSheetsConsumed, receiptKey, receipt.SubmittedAt),
                EstimateSource = "VALIDATED_COMPLETION_ESTIMATE", AppliedUsageReceiptKeys = inventory.AppliedUsageReceiptKeys.Append(receiptKey).ToArray(), AppliedUsageJobKeys = inventory.AppliedUsageJobKeys.Append(usageJobKey).ToArray(),
                Revision = checked(inventory.Revision + 1) };
            // Automatic usage cannot re-arm refill alerts and cannot clear other inventory uncertainty.
            var alert = PaperAlertPolicy.Plan(inventory, next, InventoryAction.RecordConfirmedUsage);
            inventory = Decide(alert.Inventory, at, job.Id, "APPLIED", "Validated adapter supplied completed physical sheet usage; count remains an unverified software estimate.");
            if(alert.Notification is not null) notices.Add(alert.Notification);
        }
        return new(inventory, notices);
    }
    private PaperInventory Decide(PaperInventory inventory, DateTimeOffset at, uint job, string outcome, string reason)
    {
        if(outcome == "REJECTED") safeGenerations.Remove(inventory.PrinterName);
        if(inventory.LastUsageDecision is { } old && old.JobId==job && old.Outcome==outcome && old.Reason==reason) return inventory;
        return inventory with { LastUsageDecision = new(at,job,outcome,reason) };
    }
}
