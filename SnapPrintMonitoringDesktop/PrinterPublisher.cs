using System.Text;
using System.Text.Json;

namespace SnapPrintMonitoringDesktop;

internal sealed class PrinterPublisher(HttpClient client, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, string> results = new();
    private readonly Dictionary<string, string> accepted = new();
    private readonly Dictionary<string, DateTimeOffset> retryAfter = new();
    internal string LastResult => results.Count == 0 ? "Waiting for local readings." : string.Join(Environment.NewLine, results.Values);

    internal async Task PublishAsync(IReadOnlyList<PrinterReading> readings, CancellationToken token)
    {
        foreach (var reading in readings)
        {
            string? key = reading.Queue switch
            {
                "Canon MG3000 series" => "canon",
                "EPSON L3210 Series" => "epson",
                _ => null
            };
            if (key is null) continue;
            // Strip the local evidence annotation, without changing Windows status detection.
            string state = reading.State == "READY (Windows-reported)" ? "READY" : reading.State;
            if (state == "NOT FOUND") state = "UNKNOWN";
            if (accepted.GetValueOrDefault(key) == state) continue;
            if (retryAfter.TryGetValue(key, out var next) && clock.GetUtcNow() < next) continue;
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(new
                {
                    state,
                    updatedAt = new Dictionary<string, string> { [".sv"] = "timestamp" }
                }), Encoding.UTF8, "application/json");
                using var response = await client.PutAsync(
                    $"https://snapprintmonitor-default-rtdb.firebaseio.com/printers/{key}.json", content, token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                accepted[key] = state;
                retryAfter.Remove(key);
                results[key] = $"{key}: Firebase accepted {state} at {DateTime.Now:T}";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Delivery may be ambiguous after a timeout. Retry the latest reading, not an old queued value.
                accepted.Remove(key);
                retryAfter[key] = clock.GetUtcNow().AddSeconds(15);
                results[key] = $"{key}: publish failed; retry in 15s. {ex.Message}";
                System.Diagnostics.Trace.TraceError(results[key]);
            }
        }
    }
}

