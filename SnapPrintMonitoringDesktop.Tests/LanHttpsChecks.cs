using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SnapPrintMonitoringDesktop;

internal static class LanHttpsChecks
{
    internal static void CreateCertificate()
    {
        var options = MonitorOptions.FromEnvironment();
        options.Validate();
        if (string.IsNullOrEmpty(options.CertificatePassword) || options.CertificatePassword.Length < 32)
            throw new InvalidOperationException("Development PFX requires a random password of at least 32 characters.");
        if (File.Exists(options.CertificatePath)) throw new InvalidOperationException("Certificate already exists; use a new directory.");
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=SnapPrint development monitor", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(options.Address);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(options.CertificatePath!, certificate.Export(X509ContentType.Pfx, options.CertificatePassword));
        File.WriteAllBytes(Path.ChangeExtension(options.CertificatePath, ".cer")!, certificate.Export(X509ContentType.Cert));
        Console.WriteLine($"Created 30-day development certificate with IP SAN {options.Address}. No certificate stores modified.");
    }
    internal static async Task RunAsync()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
        var options = MonitorOptions.FromEnvironment();
        using var trusted = X509CertificateLoader.LoadCertificateFromFile(Path.ChangeExtension(options.CertificatePath, ".cer")!);
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            VerificationFlags = X509VerificationFlags.NoFlag,
            // This isolated, short-lived self-signed development certificate has no revocation service.
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        policy.CustomTrustStore.Add(trusted);
        policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        using var handler = new SocketsHttpHandler { UseProxy = false };
        handler.SslOptions.CertificateChainPolicy = policy;
        // No validation callback: platform TLS validation still enforces SAN and validity.
        using var client = new HttpClient(handler) { BaseAddress = new Uri(options.Url), Timeout = TimeSpan.FromSeconds(10) };
        var store = new SnapshotStore();
        store.Update(new PrinterSnapshot(DateTimeOffset.UtcNow, WindowsPrinterMonitor.Read(), null)
        { PaperInventories = PaperInventoryService.PrinterNames.Select(n => PaperInventory.Empty(n)).ToArray() });
        await using var server = new LocalMonitoringServer();
        await server.StartAsync(options, store, CancellationToken.None);
        Check(true, "Encrypted PFX loaded; HTTPS server started at " + options.Url);
        Check((await client.GetAsync("/api/health")).StatusCode == HttpStatusCode.Unauthorized, "HTTPS health without token returns 401");
        Check((await client.GetAsync("/api/printers")).StatusCode == HttpStatusCode.Unauthorized, "HTTPS printers without token returns 401");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Check((await client.GetAsync("/api/health")).StatusCode == HttpStatusCode.Unauthorized, "Wrong token returns 401");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        using var health = JsonDocument.Parse(await client.GetStringAsync("/api/health"));
        Check(health.RootElement.GetProperty("status").GetString() == "running", "Authenticated HTTPS health returns running");
        using var printers = JsonDocument.Parse(await client.GetStringAsync("/api/printers"));
        Check(printers.RootElement.GetProperty("discoverySucceeded").GetBoolean(), "Authenticated HTTPS printers returns real Windows snapshot");
        Console.WriteLine($"Windows queues discovered: {printers.RootElement.GetProperty("printers").GetArrayLength()}");
        Check(printers.RootElement.GetProperty("paperInventories").GetArrayLength() == 2 && !printers.RootElement.GetProperty("paperInventories")[0].GetProperty("automaticDeductionEnabled").GetBoolean(), "Authenticated HTTPS exposes conservative inventory fields");
        // Prove default OS trust rejects this uninstalled self-signed certificate.
        using var defaultHandler = new SocketsHttpHandler { UseProxy = false };
        using var defaultClient = new HttpClient(defaultHandler) { Timeout = TimeSpan.FromSeconds(10) };
        bool rejected = false;
        try { await defaultClient.GetAsync(options.Url + "/api/health"); } catch (HttpRequestException) { rejected = true; }
        Check(rejected, "OS default trust rejects certificate that is not installed");
        // Same listener, wrong hostname/IP: explicit certificate trust must still reject SAN mismatch.
        using var mismatchHandler = new SocketsHttpHandler { UseProxy = false };
        mismatchHandler.SslOptions.CertificateChainPolicy = policy;
        mismatchHandler.ConnectCallback = async (_, cancellation) =>
        {
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            try { await socket.ConnectAsync(options.Address, options.Port, cancellation); return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        };
        using var mismatchClient = new HttpClient(mismatchHandler) { Timeout = TimeSpan.FromSeconds(10) };
        rejected = false;
        try { await mismatchClient.GetAsync($"https://127.0.0.1:{options.Port}/api/health"); } catch (HttpRequestException) { rejected = true; }
        Check(rejected, "Wrong IP SAN is rejected with explicit certificate trust");
        await server.DisposeAsync();
        using var shutdownHandler = new SocketsHttpHandler { UseProxy = false };
        shutdownHandler.SslOptions.CertificateChainPolicy = policy;
        using var shutdownClient = new HttpClient(shutdownHandler) { Timeout = TimeSpan.FromSeconds(5) };
        rejected = false;
        try { await shutdownClient.GetAsync(options.Url + "/api/health"); } catch (HttpRequestException) { rejected = true; }
        Check(rejected, "HTTPS listener stops on disposal");
        Console.WriteLine($"PASS: {checks} LAN HTTPS checks; self-address allowlist accepted. No firewall or trust store changes.");
    }
}
