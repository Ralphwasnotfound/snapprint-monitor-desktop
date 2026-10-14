using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SnapPrintMonitoringDesktop;

internal sealed class MonitorConfigurationException(string message) : Exception(message);
internal sealed record MonitorOptions(IPAddress Address, int Port, string Token, string? CertificatePath,
    string? CertificatePassword, IPAddress[] AllowedClients)
{
    public bool IsLan => !IPAddress.IsLoopback(Address);
    public string Url => $"{(IsLan ? "https" : "http")}://{Address}:{Port}";
    public static MonitorOptions FromEnvironment()
    {
        var address = IPAddress.Parse(Environment.GetEnvironmentVariable("SNAPMON_BIND_IP") ?? "127.0.0.1");
        var port = int.Parse(Environment.GetEnvironmentVariable("SNAPMON_PORT") ?? "5050");
        var clients = (Environment.GetEnvironmentVariable("SNAPMON_ALLOWED_CLIENTS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(IPAddress.Parse).ToArray();
        return new(address, port, Environment.GetEnvironmentVariable("SNAPMON_TOKEN") ?? "",
            Environment.GetEnvironmentVariable("SNAPMON_CERT_PATH"),
            Environment.GetEnvironmentVariable("SNAPMON_CERT_PASSWORD"), clients);
    }
    public void Validate()
    {
        if (Port is < 1 or > 65535) throw new MonitorConfigurationException("Port must be between 1 and 65535.");
        if (Encoding.UTF8.GetByteCount(Token) < 32) throw new MonitorConfigurationException("Set SNAPMON_TOKEN to a random token of at least 32 bytes.");
        if (!Address.Equals(IPAddress.Loopback) && !IsPrivateV4(Address))
            throw new MonitorConfigurationException("Use 127.0.0.1 or an explicit private IPv4 LAN address.");
        if (IsLan)
        {
            if (!NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up &&
                n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(Address))))
                throw new MonitorConfigurationException("The configured LAN address is not on an active local adapter.");
            if (string.IsNullOrWhiteSpace(CertificatePath) || AllowedClients.Length == 0 || AllowedClients.Any(a => !IsPrivateV4(a)))
                throw new MonitorConfigurationException("LAN mode requires an HTTPS certificate and explicit private IPv4 allowed clients.");
        }
    }
    private static bool IsPrivateV4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168);
    }
}
internal sealed class LocalMonitoringServer : IAsyncDisposable
{
    private WebApplication? app;
    public async Task StartAsync(MonitorOptions options, SnapshotStore store, CancellationToken token)
    {
        options.Validate();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        // Never log requests, headers, certificate passwords, or configuration values.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = 0;
            k.Limits.MaxConcurrentConnections = 32;
            k.Listen(options.Address, options.Port, listen =>
            {
                if (options.IsLan) listen.UseHttps(options.CertificatePath!, options.CertificatePassword);
            });
        });
        var candidate = builder.Build();
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + options.Token));
        candidate.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || (options.IsLan ? !options.AllowedClients.Contains(remote) : !IPAddress.IsLoopback(remote)))
            { context.Response.StatusCode = 403; return; }
            var supplied = context.Request.Headers.Authorization.ToString();
            if (!CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            { context.Response.StatusCode = 401; context.Response.Headers.WWWAuthenticate = "Bearer"; return; }
            await next(context);
        });
        candidate.MapGet("/api/health", () => Results.Json(new { status = "running", timestamp = DateTimeOffset.UtcNow,
            discoverySucceeded = store.Current.DiscoverySucceeded, lastUpdated = store.Current.LastUpdated }));
        candidate.MapGet("/api/printers", () => Results.Json(store.Current));
        try { await candidate.StartAsync(token); app = candidate; }
        catch { await candidate.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (app is null) return;
        var stopping = app; app = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await stopping.StopAsync(timeout.Token); }
        finally { await stopping.DisposeAsync(); }
    }
}
