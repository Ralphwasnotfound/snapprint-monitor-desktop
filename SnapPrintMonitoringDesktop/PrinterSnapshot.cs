using System.Text.Json.Serialization;

namespace SnapPrintMonitoringDesktop;

internal sealed record PrinterReading(
    [property: JsonPropertyName("name")] string Queue,
    [property: JsonPropertyName("reportedStatus")] string State,
    [property: JsonPropertyName("diagnostics")] string Detail)
{
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
    public bool PhysicalReadinessVerified => false;
    public string PhysicalStatus => "UNKNOWN";
    public StatusEvidence? Evidence { get; init; }
    public PaperInventory? PaperInventory { get; init; }
    [JsonIgnore] internal IReadOnlyDictionary<string, string> RawSignals { get; init; } = new Dictionary<string, string>();
}
internal sealed record PrinterSnapshot(DateTimeOffset LastUpdated, IReadOnlyList<PrinterReading> Printers, string? DiscoveryError)
{
    public IReadOnlyList<PaperInventory> PaperInventories { get; init; } = Array.Empty<PaperInventory>();
    public string? InventoryError { get; init; }
    public bool DiscoverySucceeded => DiscoveryError is null;
    public static PrinterSnapshot Initial => new(DateTimeOffset.UtcNow, Array.Empty<PrinterReading>(), "Discovery has not completed.");
}
internal sealed class SnapshotStore
{
    private PrinterSnapshot current = PrinterSnapshot.Initial;
    public PrinterSnapshot Current => Volatile.Read(ref current);
    public void Update(PrinterSnapshot snapshot) => Volatile.Write(ref current, snapshot);
}
