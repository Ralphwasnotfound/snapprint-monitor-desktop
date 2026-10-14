using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using SnapPrintMonitoringDesktop;

if (args.Contains("--create-dev-cert")) { LanHttpsChecks.CreateCertificate(); return; }
if (args.Contains("--lan-https")) { await LanHttpsChecks.RunAsync(); return; }
if (args.Contains("--capture-signals"))
{
    Console.WriteLine(JsonSerializer.Serialize(new PrinterSnapshot(DateTimeOffset.UtcNow, WindowsPrinterMonitor.Read(), null), new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    return;
}
if (args.Contains("--ui-checks")) { DashboardChecks.Run(); return; }
PrintSettingsChecks.Run();
SpoolerEstimateChecks.Run();
PrintHistoryChecks.Run();
PaperSettingsChecks.Run();
PaperInputChecks.Run();
AutomaticUsageChecks.Run();
InventoryChecks.Run();
NotificationChecks.Run();
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
Check(WindowsPrinterMonitor.Map(false, 3, 3, 1, 1) == "UNKNOWN", "Idle must not imply readiness");
Check(WindowsPrinterMonitor.Map(true, 3, 3, 1, 1) == "OFFLINE", "Offline");
Check(WindowsPrinterMonitor.Map(false, 4, 4, 1, 1) == "PRINTING", "Printing");
Check(WindowsPrinterMonitor.Map(false, 3, 3, 7, 1) == "ERROR", "Door-open queue report");
Check(WindowsPrinterMonitor.Map(null, -1, -1, -1, -1) == "UNKNOWN", "Missing driver status");
Check(WindowsPrinterMonitor.Map(false, 5, 5, 1, 1) == "NOT READY", "Warmup");
Check(WindowsPrinterMonitor.Map(false, 3, 3, 2, 2) == "UNKNOWN", "WMI No Error=2 regression");
Check(WindowsPrinterMonitor.Map(false, 3, 3, 9, 9) == "ERROR", "Service Requested=9 is queue fault, not physical offline");
Check(PrinterSignals.MapNative(1024) == "PRINTING", "Native printing flag is not error");
Check(PrinterSignals.MapNative(512) != "ERROR", "Native busy flag is not error");
var present = new UsbEvidence("EXACT_QUEUE_DEVICE_ID", @"USBPRINT\TEST\1", true, 8, 0, "Fixture");
var absent = present with { Present = false, DeviceStatus = null, ProblemCode = null };
StatusEvidence Evidence(UsbEvidence device, string queue = "NORMAL_REPORTED", params JobReading[] jobs) =>
    PrinterSignals.Classify(device, new Dictionary<string,string> { ["GetPrinter.QueueClassification"] = queue }, jobs, "AVAILABLE", queue);
Check(Evidence(present).PhysicalStatus == "UNKNOWN" && Evidence(present).ConnectionStatus == "USB_PRESENT", "Powered ON fixture: presence not physical readiness");
Check(Evidence(absent, "OFFLINE").PhysicalStatus == "UNKNOWN" && Evidence(absent).ConnectionStatus == "USB_NOT_PRESENT", "Powered OFF fixture: absence not proof of power state");
Check(Evidence(absent).PhysicalStatus == "UNKNOWN", "USB disconnected fixture: cannot distinguish powered off");
Check(Evidence(present).PhysicalStatus != "ONLINE", "Normal idle queue not physical online");
Check(Evidence(present, "NORMAL_REPORTED", new JobReading(1,128,1,1)).Jobs[0].CompletionReported, "Successful printing fixture completion retained separately");
Check(Evidence(present, "ERROR", new JobReading(1,2,1,0)).PhysicalStatus == "UNKNOWN", "Transient spooler errors not physical error");
Check(Evidence(present with { Mapping = "AMBIGUOUS_SHARED_DEVICE" }).ConnectionStatus == "UNKNOWN", "Ambiguous mapping");
Check(Evidence(present with { Mapping = "UNMAPPED" }).ConnectionStatus == "UNKNOWN", "USB001 alone cannot identify device");
Check(Evidence(present, "ERROR").ErrorAttribution.Contains("GetPrinter.QueueClassification"), "Error attribution names the source");
Check(Evidence(present, "NORMAL_REPORTED", new JobReading(7,128|2,1,1)).Jobs[0].ErrorReported, "Completion and error may coexist; both retained");
Check(Evidence(present with { Present = null }).ConnectionStatus == "UNKNOWN", "PnP query failure is not device absence");
Check(WindowsPrinterMonitor.Map(false, 3, 3, 3, 3) == "WARNING", "Low paper is warning, not a failed print");
Check(WindowsPrinterMonitor.Map(false, 3, 3, 3, 7) == "ERROR", "Fault is not hidden by simultaneous low-paper warning");
Check(WindowsPrinterMonitor.Map(false, 4, 2, 2, 0) == "PRINTING", "Captured Canon printing with No Error=2 regression");
Check(!new JobReading(6,8208,0,1).ErrorReported, "Captured Canon job 6 flags are not error");
Check(PrinterSignals.ResolveQueueStatus(["PRINTING", "NORMAL_REPORTED", "NORMAL_REPORTED"]) == "PRINTING", "Zero queue flags do not contradict printing");
Check(PrinterSignals.ResolveQueueStatus(["ERROR", "NORMAL_REPORTED"]) == "CONFLICTING_WINDOWS_REPORTS", "Contradictory error reports retained explicitly");
var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
var options = new MonitorOptions(IPAddress.Loopback, port, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), null, null, []);
foreach (var invalid in new[] { options with { Token = "short" }, options with { Port = 0 }, options with { Address = IPAddress.Any }, options with { Address = IPAddress.Parse("8.8.8.8") } })
{
    bool rejected = false;
    try { invalid.Validate(); } catch (MonitorConfigurationException) { rejected = true; }
    Check(rejected, "Unsafe configuration rejected");
}
var store = new SnapshotStore(); store.Update(new(DateTimeOffset.UtcNow, [], null));
var server = new LocalMonitoringServer();
await server.StartAsync(options, store, CancellationToken.None);
using var client = new HttpClient { BaseAddress = new Uri(options.Url), Timeout = TimeSpan.FromSeconds(5) };
Check((await client.GetAsync("/api/health")).StatusCode == HttpStatusCode.Unauthorized, "Health authentication");
Check((await client.GetAsync("/api/printers")).StatusCode == HttpStatusCode.Unauthorized, "Printers authentication");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
Check((await client.GetAsync("/api/health")).StatusCode == HttpStatusCode.Unauthorized, "Wrong token");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
using var health = JsonDocument.Parse(await client.GetStringAsync("/api/health"));
Check(health.RootElement.GetProperty("status").GetString() == "running", "Health JSON");
using var empty = JsonDocument.Parse(await client.GetStringAsync("/api/printers"));
Check(empty.RootElement.GetProperty("printers").GetArrayLength() == 0 && empty.RootElement.GetProperty("discoverySucceeded").GetBoolean(), "Zero printers");
store.Update(new(DateTimeOffset.UtcNow, [new PrinterReading("USB test queue", "UNKNOWN", "Driver idle") { Evidence = Evidence(present, "NORMAL_REPORTED", new JobReading(7, 128, 1, 1)) }], null));
using var printers = JsonDocument.Parse(await client.GetStringAsync("/api/printers"));
var printer = printers.RootElement.GetProperty("printers")[0];
Check(printer.GetProperty("name").GetString() == "USB test queue" && printer.GetProperty("reportedStatus").GetString() == "UNKNOWN" && printer.TryGetProperty("lastUpdated", out _) && printer.GetProperty("diagnostics").GetString() == "Driver idle", "Printer response");
Check(printer.GetProperty("physicalStatus").GetString() == "UNKNOWN" && !printer.GetProperty("physicalReadinessVerified").GetBoolean() && printer.GetProperty("evidence").GetProperty("connectionStatus").GetString() == "USB_PRESENT", "API separates physical and USB status");
Check(printer.GetProperty("evidence").GetProperty("jobs")[0].GetProperty("rawStatus").GetUInt32() == 128, "API exposes numeric job status separately");
var inventoryFixture = PaperInventory.Empty("Canon MG3000 series") with { EstimatedSheetsRemaining = 2, LastInventoryUpdate = DateTimeOffset.UtcNow, SynchronizationStatus = "RECONCILIATION_REQUIRED" };
store.Update(new(DateTimeOffset.UtcNow, [new PrinterReading("Canon MG3000 series","UNKNOWN","fixture") { PaperInventory = inventoryFixture }],null) { PaperInventories = [inventoryFixture,PaperInventory.Empty("EPSON L3210 Series")] });
using var inventoryResponse = JsonDocument.Parse(await client.GetStringAsync("/api/printers"));
Check(inventoryResponse.RootElement.GetProperty("paperInventories").GetArrayLength()==2 && inventoryResponse.RootElement.GetProperty("printers")[0].GetProperty("paperInventory").GetProperty("lowPaperWarning").GetBoolean(), "Authenticated inventory JSON includes estimates and warnings");
Check(!inventoryResponse.RootElement.GetProperty("paperInventories")[0].GetProperty("automaticDeductionEnabled").GetBoolean(), "API does not imply automatic or physically verified counting");
Check((await client.PostAsync("/api/printers", null)).StatusCode==HttpStatusCode.MethodNotAllowed, "Inventory API remains read only");
store.Update(new(DateTimeOffset.UtcNow, [], "Spooler unavailable"));
using var failed = JsonDocument.Parse(await client.GetStringAsync("/api/printers"));
Check(!failed.RootElement.GetProperty("discoverySucceeded").GetBoolean(), "Discovery failure");
Check((await client.PostAsync("/api/printers", null)).StatusCode == HttpStatusCode.MethodNotAllowed, "Read only");
await using (var conflicting = new LocalMonitoringServer())
{
    bool rejected = false;
    try { await conflicting.StartAsync(options, store, CancellationToken.None); } catch (IOException) { rejected = true; }
    Check(rejected, "Port conflict");
}
await server.DisposeAsync();
await server.DisposeAsync();
await using (var restarted = new LocalMonitoringServer()) { await restarted.StartAsync(options, store, CancellationToken.None); }
bool stopped = false;
try { await client.GetAsync("/api/health"); } catch (HttpRequestException) { stopped = true; }
Check(stopped, "Shutdown releases listener");
Console.WriteLine($"PASS: {checks} checks (synthetic printer data; no physical/Android testing).");
try { var actual = WindowsPrinterMonitor.Read(); Console.WriteLine($"Live Windows discovery completed: {actual.Count} queues; no physical readiness verified."); }
catch (Exception ex) { Console.WriteLine($"Live Windows discovery unavailable: {ex.GetType().Name}"); }
