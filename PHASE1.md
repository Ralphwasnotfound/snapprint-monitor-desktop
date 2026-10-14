# Phase 1 — offline Windows printer monitoring

## Detection and changes

The old WMI query discarded every queue except two exact Canon/Epson names, returned placeholder NOT FOUND entries, and started Firebase publishing on every launch. Discovery now returns all Win32_Printer queues, including USB and virtual queues, with native EnumPrinters installed-queue fallback when WMI is unavailable. Empty successful discovery is distinct from failed discovery. Offline, printing, error and not-ready reports are mapped; idle/no-error reports remain UNKNOWN because drivers may cache defaults. Physical readiness is never claimed.

- WindowsPrinterMonitor.cs: dynamic discovery and conservative mapping; WMI COM cleanup retained.
- PrinterSnapshot.cs: timestamped readings and atomic snapshots, discovery diagnostics.
- LocalMonitoringServer.cs: authenticated read-only Kestrel API and lifecycle cleanup.
- Form1.cs: background polling, server status/address/errors, bounded exit cleanup; no Firebase calls.
- csproj: built-in ASP.NET Core framework reference replaces unused Firebase NuGet reference. PrinterPublisher.cs remains unchanged for rollback (it uses HttpClient and requires no Firebase package). Restoring the old publishing loop is a deliberate source change, not a runtime default.
- Tests project and solution: dependency-free executable checks using real HTTP with synthetic snapshots.

## Configuration and API

Environment variables are read at launch:

| Variable | Default / meaning |
|---|---|
| SNAPMON_BIND_IP | 127.0.0.1; explicit RFC1918 IPv4 required for LAN |
| SNAPMON_PORT | 5050 |
| SNAPMON_TOKEN | Required; at least 32 UTF-8 bytes; generate randomly |
| SNAPMON_ALLOWED_CLIENTS | LAN: required comma-separated trusted device IPv4 addresses |
| SNAPMON_CERT_PATH | LAN: required server PFX file |
| SNAPMON_CERT_PASSWORD | PFX password; optional for unencrypted PFX |

GET /api/health and GET /api/printers both require Authorization: Bearer TOKEN. Missing/wrong token: 401. Disallowed source IP: 403. POST is unsupported (405). No print/management endpoints. Health indicates server liveness separately from discovery success. Printers response:

```json
{"lastUpdated":"2026-10-14T09:00:00Z","printers":[{"name":"USB printer","reportedStatus":"UNKNOWN","diagnostics":"Port=USB001; WorkOffline=False; Status=3; Extended=3; Error=1/1","lastUpdated":"2026-10-14T09:00:00Z","physicalReadinessVerified":false}],"discoveryError":null,"discoverySucceeded":true}
```

Discovery failure returns an empty list with discoverySucceeded=false and an error; consumers must not interpret this as a successful zero-printer discovery. Polling is every 3 seconds. Inspect lastUpdated for stale data. WMI can block beyond the poll interval; shutdown stops the server and waits at most five seconds for polling.

## Exact local test commands (PowerShell)

Run from the project root. No internet is required for restore with the installed .NET 10 targeting packs:

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet restore .\SnapPrintMonitoringDesktop\SnapPrintMonitoringDesktop.csproj --source C:\CODE\snapprint-monitor-desktop
dotnet restore .\SnapPrintMonitoringDesktop.Tests\SnapPrintMonitoringDesktop.Tests.csproj --source C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project .\SnapPrintMonitoringDesktop.Tests --no-build
$env:SNAPMON_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:SNAPMON_BIND_IP = '127.0.0.1'
$env:SNAPMON_PORT = '5050'
Start-Process .\SnapPrintMonitoringDesktop\bin\Debug\net10.0-windows\SnapPrintMonitoringDesktop.exe
$headers = @{ Authorization = 'Bearer ' + $env:SNAPMON_TOKEN }
Invoke-RestMethod http://127.0.0.1:5050/api/health -Headers $headers
Invoke-RestMethod http://127.0.0.1:5050/api/printers -Headers $headers | ConvertTo-Json -Depth 6
# Must fail with 401:
Invoke-WebRequest http://127.0.0.1:5050/api/health
Get-Printer | Select-Object Name,PortName,PrinterStatus
# Close the monitor window, then this must fail to connect:
Invoke-RestMethod http://127.0.0.1:5050/api/health -Headers $headers
```

Keep the token in this shell; do not print it, put it in a URL, commit it, or use a literal token in shell history. Provision trusted devices through a secure administrative channel. Rotate by generating a new token and restarting the monitor.

## Wi-Fi setup and testing

Use a dedicated trusted staff/admin network or VLAN with separation from customers. A shared customer SSID is not inherently trusted. Reserve kiosk and monitoring-device DHCP addresses. Bind only to the kiosk's trusted adapter IP. The server also checks the explicit allowed client addresses. IP allowlisting supplements authentication; it is not identity proof. Never use router port forwarding, wildcard listeners, public firewall rules or disabled firewall rules. Loopback HTTP is for same-PC testing only; LAN requires HTTPS to protect the token from other Wi-Fi users.

Provision a PFX certificate with a private key and subject alternative name matching the kiosk IP or a locally resolvable hostname; install the issuer/certificate trust on the monitoring device. Protect PFX permissions and its password. Android certificate trust/pinning integration is deferred to its own phase. Do not disable certificate verification to get connectivity working.

Example configuration (replace sample IPs and paths, close the previous monitor first):

```powershell
$env:SNAPMON_BIND_IP = '192.168.50.10'
$env:SNAPMON_ALLOWED_CLIENTS = '192.168.50.20'
$env:SNAPMON_CERT_PATH = 'C:\Secure\snapmonitor.pfx'
$securePassword = Read-Host 'PFX password' -AsSecureString
$env:SNAPMON_CERT_PASSWORD = [Net.NetworkCredential]::new('', $securePassword).Password
Start-Process C:\CODE\snapprint-monitor-desktop\SnapPrintMonitoringDesktop\bin\Debug\net10.0-windows\SnapPrintMonitoringDesktop.exe
Remove-Item Env:SNAPMON_CERT_PASSWORD
```

Check Get-NetConnectionProfile and confirm that the chosen adapter belongs to the trusted private network. Do not reclassify a customer/public network as Private merely to enable access. In an elevated PowerShell, create a narrowly scoped rule only after configuring the trusted network (replace InterfaceAlias and addresses):

```powershell
New-NetFirewallRule -DisplayName 'SnapPrint trusted monitor HTTPS' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5050 -LocalAddress 192.168.50.10 -RemoteAddress 192.168.50.20 -Profile Private -InterfaceAlias 'Wi-Fi' -Program 'C:\CODE\snapprint-monitor-desktop\SnapPrintMonitoringDesktop\bin\Debug\net10.0-windows\SnapPrintMonitoringDesktop.exe'
# To remove this rule:
Remove-NetFirewallRule -DisplayName 'SnapPrint trusted monitor HTTPS'
```

From the allowed Windows device on Wi-Fi, after installing certificate trust and securely setting its token variable:

```powershell
$headers = @{ Authorization = 'Bearer ' + $env:SNAPMON_TOKEN }
Test-NetConnection 192.168.50.10 -Port 5050
Invoke-RestMethod https://192.168.50.10:5050/api/health -Headers $headers
Invoke-RestMethod https://192.168.50.10:5050/api/printers -Headers $headers
```

An Android HTTP test client can use the same URLs and Authorization header with trusted TLS. A browser address bar alone cannot supply the bearer header. Verify wrong/no token rejection and access rejection from an unlisted device. AP client isolation may block traffic; put the authorized devices in the administrative segment, rather than opening access for all customers.

## Limits and next step

No physical printer or Android connectivity testing is implied by automated tests. Test installed/renamed queues, disconnected USB, printer powered off, active jobs, paper errors and stopped spooler on the deployment PC. Windows driver reports may be delayed or inaccurate. Virtual printers are included. There is no retry button for server configuration; correct settings and restart. .NET 10 Desktop and ASP.NET Core runtimes are required (or publish self-contained later). No deployment, commits, Android edits or kiosk edits are performed.

Next: validate driver behavior and trusted HTTPS connectivity on the target Wi-Fi; then implement Android token provisioning, certificate validation and stale-reading handling in a separately authorized phase.
**Validation on this development machine:** solution build passed with zero warnings/errors; 16 automated checks passed. Initial live WMI discovery returned COMException; native spooler fallback subsequently discovered seven installed queues. Final build and all 20 checks passed again (including unsafe configuration rejection). LAN HTTPS, actual WinForms interaction, physical printer behavior and Android connectivity were not tested. No physical or Android testing performed.



Desktop-only LAN HTTPS follow-up: see [LAN_HTTPS.md](LAN_HTTPS.md) for secure development setup and actual successful HTTPS test results. Firewall changes and certificate trust-store installation require user confirmation before execution.

Physical-status follow-up: [PRINTER_STATUS.md](PRINTER_STATUS.md) documents corrected WMI error enums, separate USB/queue/job evidence, controlled hardware transitions and a captured successful Canon print. reportedStatus now represents conservative physical status (UNKNOWN); Windows queue reports are in evidence.queueStatus.
