using System.Text.Json;

namespace SnapPrintMonitoringDesktop;

internal sealed record PaperInventory(string PrinterName, int? EstimatedSheetsRemaining, int LowPaperThreshold,
    DateTimeOffset? LastInventoryUpdate, string SynchronizationStatus, string ReconciliationReason)
{
    public int? SheetsLoaded { get; init; }
    public long? TotalEstimatedSheetsAccountedFor { get; init; } = 0;
    public string Confidence => EstimatedSheetsRemaining is null ? "UNINITIALIZED" : EstimateSource == "VALIDATED_COMPLETION_ESTIMATE" ? "COMPLETION_ESTIMATE_UNVERIFIED" : "OPERATOR_ESTIMATE_UNVERIFIED";
    public bool LowPaperWarning => EstimatedSheetsRemaining is > 0 && EstimatedSheetsRemaining <= LowPaperThreshold;
    public bool EmptyWarning => EstimatedSheetsRemaining == 0;
    public bool ReconciliationRequired => SynchronizationStatus != "OPERATOR_BASELINE";
    public bool PhysicalCountVerified => false;
    public bool AutomaticPaperEstimateRequested { get; init; }
    public bool AutomaticDeductionEnabled { get; init; }
    public string AutomaticDeductionStatus => AutomaticDeductionEnabled ? "Validated completion adapter configured; counts remain software estimates." : "Unavailable: Windows spooler signals do not establish physical sheets consumed.";
    public AutomaticDeduction? LastAutomaticDeduction { get; init; }
    public UsageDecision? LastUsageDecision { get; init; }
    public string EstimateSource { get; init; } = "OPERATOR_ESTIMATE";
    public Guid ManualCountGeneration { get; init; }
    public IReadOnlyList<string> AppliedUsageReceiptKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AppliedUsageJobKeys { get; init; } = Array.Empty<string>();
    public string PersistenceStatus { get; init; } = "AVAILABLE";
    public string? LastAdjustmentKind { get; init; }
    public Guid? LastAdjustmentId { get; init; }
    public long Revision { get; init; }
    public PaperAlertState? Alerts { get; init; }
    internal static PaperInventory Empty(string name) => new(name, null, 3, null, "UNINITIALIZED", "Enter a counted physical sheet total.");
}
internal enum InventoryAction { SetCountedTotal, AddRefill, RecordConfirmedUsage, ChangeThreshold }
internal sealed record InventoryAdjustment(InventoryAction Action, int Sheets, int LowPaperThreshold, long ExpectedRevision, Guid RequestId, bool? AutomaticPaperEstimateRequested = null);

internal sealed class PaperInventoryService : IDisposable
{
    internal static readonly string[] PrinterNames = ["Canon MG3000 series", "EPSON L3210 Series"];
    private readonly object gate = new();
    private readonly string path;
    private readonly Action<string, string> writer;
    private FileStream? lease;
    private bool readOnly;
    private bool disposed;
    private readonly Dictionary<string, bool> canReconcile = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PaperInventory> entries = PrinterNames.ToDictionary(n => n, PaperInventory.Empty, StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? lastObservation;
    private readonly AutomaticPaperUsage automatic;
    public string? Error { get; private set; }
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapPrintMonitoringDesktop", "paper-inventory.json");
    private sealed record Document(int Version, PaperInventory[] Inventories);

    internal PaperInventoryService(string path, Action<string, string>? writer = null, ICompletedSheetUsageSource? source = null, TimeProvider? clock = null)
    {
        automatic = new AutomaticPaperUsage(source ?? WindowsSpoolerSheetSource.Instance, (clock ?? TimeProvider.System).GetUtcNow());
        this.path = Path.GetFullPath(path);
        this.writer = writer ?? AtomicWrite;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            lease = new FileStream(this.path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(this.path))
            {
                if (new FileInfo(this.path).Length > 4 * 1024 * 1024) throw new InvalidDataException();
                var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(this.path)) ?? throw new InvalidDataException();
                if (document.Version != 1 || document.Inventories is null || document.Inventories.Length != PrinterNames.Length) throw new InvalidDataException();
                var loaded = document.Inventories.ToDictionary(i => i.PrinterName, StringComparer.OrdinalIgnoreCase);
                if (PrinterNames.Any(n => !loaded.ContainsKey(n)) || loaded.Values.Any(i => i.EstimatedSheetsRemaining is < 0 or > 100000 ||
                    i.LowPaperThreshold is < 0 or > 100000 || i.SheetsLoaded is < 0 or > 100000 || i.TotalEstimatedSheetsAccountedFor is < 0 or > 409600000 || i.Revision is < 0 or > 1000000000000 || i.AppliedUsageReceiptKeys is null || i.AppliedUsageJobKeys is null ||
                    i.AppliedUsageReceiptKeys.Count != i.AppliedUsageJobKeys.Count || i.AppliedUsageReceiptKeys.Count > AutomaticPaperUsage.ReceiptLimit ||
                    i.AppliedUsageReceiptKeys.Concat(i.AppliedUsageJobKeys).Any(k => k is null || k.Length != 64 || !k.All(Uri.IsHexDigit)) || (i.EstimatedSheetsRemaining is not null && i.LastInventoryUpdate is null)))
                    throw new InvalidDataException();
                entries = loaded.ToDictionary(p => p.Key, p => p.Value with { Alerts = PaperAlertPolicy.Bootstrap(p.Value), TotalEstimatedSheetsAccountedFor = p.Value.AppliedUsageReceiptKeys.Count>0 && p.Value.TotalEstimatedSheetsAccountedFor==0 ? null : p.Value.TotalEstimatedSheetsAccountedFor }, StringComparer.OrdinalIgnoreCase);
                foreach (var name in PrinterNames)
                    if (entries[name].EstimatedSheetsRemaining is not null) entries[name] = RequireReconciliation(entries[name], "Application restart: printing or paper changes during downtime cannot be accounted for.");
                Save(entries); // Persist the restart gap even if no printer query is available.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
        {
            readOnly = true;
            Error = "Inventory storage unavailable or invalid. Check file access, another running monitor, or restore a valid backup. Existing file was not overwritten.";
        }
    }
    internal IReadOnlyList<PaperInventory> Current
    {
        get { lock (gate) return PrinterNames.Select(n => Error is null ? entries[n] with { AutomaticDeductionEnabled = automatic.Supported(n) } : entries[n] with
            { AutomaticDeductionEnabled = false, PersistenceStatus = "UNAVAILABLE", SynchronizationStatus = "RECONCILIATION_REQUIRED", ReconciliationReason = "Inventory storage unavailable; displayed count may be stale." }).ToArray(); }
    }
    internal PrinterSnapshot Attach(PrinterSnapshot snapshot)
    {
        lock (gate)
        {
            var inventory = Current;
            return snapshot with { PaperInventories = inventory, InventoryError = Error,
                Printers = snapshot.Printers.Select(p => p with { PaperInventory = inventory.FirstOrDefault(i => string.Equals(i.PrinterName, p.Queue, StringComparison.OrdinalIgnoreCase)) }).ToArray() };
        }
    }
    internal IReadOnlyList<PaperNotification> Observe(PrinterSnapshot snapshot)
    {
        lock (gate)
        {
            if (disposed) return Array.Empty<PaperNotification>();
            var gap = lastObservation is not null && (snapshot.LastUpdated - lastObservation.Value > TimeSpan.FromSeconds(15) || snapshot.LastUpdated < lastObservation);
            lastObservation = snapshot.LastUpdated;
            var candidate = new Dictionary<string, PaperInventory>(entries, StringComparer.OrdinalIgnoreCase);
            var notices = new List<PaperNotification>();
            foreach (var name in PrinterNames)
            {
                var reading = snapshot.Printers.FirstOrDefault(p => string.Equals(p.Queue, name, StringComparison.OrdinalIgnoreCase));
                var evidence = reading?.Evidence;
                var queryUnavailable = snapshot.DiscoveryError is not null || evidence is null ||
                    !evidence.JobQueryStatus.StartsWith("AVAILABLE", StringComparison.Ordinal) ||
                    evidence.Signals.Keys.Any(k => k.EndsWith("QueryError", StringComparison.Ordinal));
                var activity = evidence is not null && (evidence.Jobs.Count > 0 || evidence.QueueStatus == "PRINTING" ||
                    evidence.Signals.GetValueOrDefault("PrintQueue.NumberOfJobs") is string count && count != "0" ||
                    evidence.Signals.GetValueOrDefault("Win32_Printer.PrinterStatus") == "4");
                canReconcile[name] = !queryUnavailable && !activity;
                var usage = automatic.Process(candidate[name], reading, snapshot.LastUpdated, gap || queryUnavailable);
                candidate[name] = usage.Inventory;
                notices.AddRange(usage.Notifications);
                string? reason = activity ? "Windows print activity observed; counts remain estimates. See the per-job usage decision for accepted or rejected sheet accounting. Recount when uncertain." :
                    queryUnavailable ? "Printer/job monitoring unavailable; unseen sheet consumption is possible. Count remaining sheets." :
                    gap ? "Monitoring gap longer than 15 seconds or clock change; unseen printing is possible. Count remaining sheets." : null;
                if (reason is not null)
                {
                    candidate[name] = RequireReconciliation(candidate[name], reason);
                }
            }
            if (candidate.Any(p => p.Value != entries[p.Key]))
            {
                if (!readOnly && Save(candidate)) { entries = candidate; return notices; }
                foreach (var name in PrinterNames) entries[name] = RequireReconciliation(entries[name], "Monitoring state could not be persisted; no automatic deduction was applied.");
            }
            return Array.Empty<PaperNotification>();
        }
    }
    private static PaperInventory RequireReconciliation(PaperInventory entry, string reason)
    {
        var status = entry.EstimatedSheetsRemaining is null ? "UNINITIALIZED" : "RECONCILIATION_REQUIRED";
        // Sticky until an idle operator recount; repeated job observations do not create deductions or revisions.
        if (entry.SynchronizationStatus == status && entry.ReconciliationReason == reason) return entry;
        return entry with { SynchronizationStatus = status, ReconciliationReason = reason, Revision = checked(entry.Revision + 1) };
    }
    internal PaperNotification? Adjust(string name, InventoryAdjustment adjustment, DateTimeOffset now)
    {
        lock (gate)
        {
            if (disposed) throw new InvalidOperationException("Inventory service has stopped.");
            if (readOnly) throw new InvalidOperationException(Error);
            if (!entries.TryGetValue(name, out var old)) throw new InvalidOperationException("Unsupported inventory printer.");
            if (old.LastAdjustmentId == adjustment.RequestId) return null; // Immediate duplicate UI request is idempotent, including across restart.
            if (old.Revision != adjustment.ExpectedRevision) throw new InvalidOperationException("Inventory changed while this window was open. Reopen it and review the latest count.");
            if (adjustment.RequestId == Guid.Empty || adjustment.Sheets is < 0 or > 100000 || adjustment.LowPaperThreshold is < 0 or > 100000 || !Enum.IsDefined(adjustment.Action))
                throw new InvalidOperationException("Enter whole sheet counts and thresholds between 0 and 100000.");
            int? count = adjustment.Action switch
            {
                InventoryAction.SetCountedTotal => adjustment.Sheets,
                InventoryAction.AddRefill when old.EstimatedSheetsRemaining is not null && adjustment.Sheets > 0 => checked(old.EstimatedSheetsRemaining + adjustment.Sheets),
                InventoryAction.RecordConfirmedUsage when old.EstimatedSheetsRemaining is not null && adjustment.Sheets > 0 => old.EstimatedSheetsRemaining - adjustment.Sheets,
                InventoryAction.ChangeThreshold => old.EstimatedSheetsRemaining,
                _ => throw new InvalidOperationException("Set a counted total first; refill/usage changes require a positive sheet count.")
            };
            if (count is < 0 or > 100000) throw new InvalidOperationException("The resulting estimate must be between 0 and 100000. Recount instead of allowing a negative count.");
            var mayReconcile = canReconcile.GetValueOrDefault(name) && lastObservation is not null && now >= lastObservation.Value && now - lastObservation.Value <= TimeSpan.FromSeconds(15);
            var next = old with { EstimatedSheetsRemaining = count, LowPaperThreshold = adjustment.LowPaperThreshold,
                LastInventoryUpdate = adjustment.Action == InventoryAction.ChangeThreshold ? old.LastInventoryUpdate : now,
                LastAdjustmentId = adjustment.RequestId, LastAdjustmentKind = adjustment.Action.ToString(), Revision = checked(old.Revision + 1),
                AutomaticPaperEstimateRequested = adjustment.AutomaticPaperEstimateRequested ?? old.AutomaticPaperEstimateRequested,
                SheetsLoaded = adjustment.Action == InventoryAction.SetCountedTotal ? adjustment.Sheets : old.SheetsLoaded };
            if (adjustment.Action != InventoryAction.ChangeThreshold)
                next = next with { ManualCountGeneration = Guid.NewGuid(), EstimateSource = "OPERATOR_ESTIMATE" };
            if (Error is not null && adjustment.Action != InventoryAction.SetCountedTotal)
                next = RequireReconciliation(next, "Storage was unavailable; recount remaining sheets.");
            if (adjustment.Action == InventoryAction.SetCountedTotal)
                next = next with { SynchronizationStatus = mayReconcile ? "OPERATOR_BASELINE" : "RECONCILIATION_REQUIRED",
                    ReconciliationReason = mayReconcile ? "Operator-counted baseline; sensorless and not continuously verified. Short jobs or standalone copying may be missed." :
                    "Count saved while job monitoring is active, unavailable or stale. Recount with fresh idle monitoring." };
            var plan = PaperAlertPolicy.Plan(old, next, adjustment.Action);
            next = plan.Inventory;
            var candidate = new Dictionary<string, PaperInventory>(entries, StringComparer.OrdinalIgnoreCase) { [name] = next };
            if (!Save(candidate)) throw new InvalidOperationException("Inventory could not be saved. No adjustment was applied. Check local storage and retry.");
            entries = candidate;
            return plan.Notification; // Only emit after count AND eligibility were saved successfully.
        }
    }
    private bool Save(Dictionary<string, PaperInventory> candidate)
    {
        try { writer(path, JsonSerializer.Serialize(new Document(1, candidate.Values.ToArray()), new JsonSerializerOptions { WriteIndented = true })); Error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = "Inventory save failed. Check local storage access and free space; counts require reconciliation."; return false; }
    }
    private static void AtomicWrite(string path, string text)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) { writer.Write(text); writer.Flush(); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose() { lock (gate) { disposed = true; lease?.Dispose(); lease = null; } }
}
