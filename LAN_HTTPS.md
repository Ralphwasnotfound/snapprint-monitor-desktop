# Desktop-only LAN HTTPS validation

The existing server supports explicit private IPv4 binding, encrypted PFX certificates, bearer authentication and exact source-IP allowlisting. No production server changes were needed. Verified Ethernet address: 192.168.254.120; Private profile; test port: 5050. Binding a private IP does not alone establish that a network is trusted. Keep the administrative network separated from customers.

For desktop-only requests to its LAN address, Windows used 192.168.254.120 as the source address. Setting SNAPMON_ALLOWED_CLIENTS to that same address passed the real test. This is a temporary self-test allowlist, not permission for all devices on the subnet. Other devices remain disallowed. IP allowlisting supplements bearer authentication and TLS.

## Exact PowerShell commands

Use PowerShell 7 (`pwsh`), with the existing monitor closed if it uses 5050. No execution-policy bypass, firewall changes, certificate-store installs, router changes or TLS bypasses are included.

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false
pwsh -NoProfile -File .\scripts\Test-LanHttps.ps1 -Address 192.168.254.120 -Port 5050
```

The script creates a fresh directory under .local-https, removes inherited permissions before writing secrets, and grants access only to the current user and SYSTEM. It creates a 3072-bit RSA/SHA-256 self-signed server certificate valid for 30 days, with an **IP SAN**, not a DNS SAN containing IP text. The certificate has server-authentication EKU and is not a CA. The PFX is encrypted with a random 32-byte password. Both secrets exist only in the process environment during testing; no token/password is printed, written to disk, or passed as a command-line argument. The script restores prior environment settings and removes its generated PFX/public CER on exit. The public CER contains no private key. .local-https is ignored by Git.

To validate and then run the actual WinForms application using the same temporary configuration:

```powershell
pwsh -NoProfile -File .\scripts\Test-LanHttps.ps1 -Address 192.168.254.120 -Port 5050 -LaunchDesktop
```

Close its window to finish cleanup. This optional UI command was not performed in this run. Do not terminate the script forcibly: that can prevent finally cleanup. The credentials belong to this temporary session; rerunning creates new credentials and certificate. The environment approach matches the current server configuration and assumes a trusted local user account; same-user processes/admins can inspect process secrets. For persistent deployment, use a separate credential-provisioning design rather than writing secrets to scripts or repository files.

Settings used internally by the script (safe to show values for non-secret settings):

```powershell
$env:SNAPMON_BIND_IP = '192.168.254.120'
$env:SNAPMON_PORT = '5050'
$env:SNAPMON_ALLOWED_CLIENTS = '192.168.254.120'
# The script sets SNAPMON_CERT_PATH to an absolute protected path ending in monitor.pfx.
# SNAPMON_CERT_PASSWORD and SNAPMON_TOKEN are randomly generated, never literal values.
# Equivalent secure in-memory generation:
$env:SNAPMON_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:SNAPMON_CERT_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Do not manually set just these variables and expect a certificate to exist: use the complete script, which creates the protected directory, PFX and public certificate and validates startup. Existing configuration is read only at server launch. The LAN IP must still be assigned to an active adapter. If DHCP changes it, regenerate the certificate and update the binding/allowlist.

## TLS verification without installing trust

The .NET test client uses CustomRootTrust with exactly the generated public certificate in its in-memory trust collection. It retains platform certificate identity/SAN, validity and server-authentication checks; no permissive certificate callback is used. Default OS trust is independently tested and rejects the uninstalled self-signed certificate. Another negative test connects to the same server using an incorrect target IP and confirms SAN mismatch rejection. This certificate has no revocation distribution service; the isolated 30-day development trust policy does not perform revocation lookups. This is scoped development trust, not a production CA/revocation policy.

Ordinary Invoke-RestMethod and browsers will reject the self-signed certificate because it was not installed. Do not use -SkipCertificateCheck, curl -k, or a callback returning true. Use the provided .NET test client for store-free validation. If OS/browser trust or cross-device access is needed later, obtain explicit approval before installing a certificate into a trust store or modifying a firewall rule. The earlier generic PHASE1 firewall examples are documentation only and must not be executed without that approval.

Microsoft references: [CertificateChainPolicy](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions.certificatechainpolicy?view=net-10.0), [TLS validation practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices).

## Actual results

- Ethernet address 192.168.254.120 and Private network profile verified.
- Build passed with zero warnings and errors.
- Existing 20 status/HTTP/lifecycle checks passed.
- 9 real HTTPS checks passed on port 5050: encrypted certificate loading/startup; health and printers without token return 401; wrong token returns 401; authenticated health and printers succeed; uninstalled certificate is rejected by OS default trust; IP SAN mismatch is rejected; disposal stops the HTTPS listener.
- Printer endpoint used live Windows discovery and returned seven queues; physical readiness was not tested.
- Own LAN IP as allowed client was verified by successful authenticated calls.
- Temporary certificate files were removed after the test.

Only same-PC traffic to its actual LAN IP was tested. This validates TLS, authentication and server binding, but does not prove that another device can traverse the router/firewall. No WinForms UI interaction, Android connectivity, external-device connectivity, physical printer tests, commits, pushes or deployment were performed. No firewall or certificate-store changes were made.

Changed files: Tests/Program.cs selects certificate/setup and HTTPS modes; Tests/LanHttpsChecks.cs implements scoped TLS validation and certificate generation; scripts/Test-LanHttps.ps1 protects private material and orchestrates tests/cleanup; .gitignore excludes temporary certificates; this guide and PHASE1.md link document results. Printer/server behavior and Firebase default are unchanged.
