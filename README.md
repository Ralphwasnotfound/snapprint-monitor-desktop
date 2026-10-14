# Print and Snap Monitor

Windows desktop printer monitoring for **Canon MG3000 series** and **EPSON L3210 Series**, using an offline local network. Version **1.0.0** targets Windows x64 and .NET 10. The release package is self-contained: no separate .NET install is required.

## Features

- USB device connection evidence and Windows queue/error reporting, with physical readiness kept Unknown when not verified.
- Current Printing and persistent local Print History, with Windows-reported counters and available requested driver settings.
- Simple per-printer paper settings: Sheets Loaded, Low Paper Alert At, optional estimate preference and Save Paper Settings.
- LOW PAPER / PAPER EMPTY tray notifications with persisted suppression and refill rearm; safe Test notification button.
- Shared per-printer inventory summaries in job views; authenticated read-only local API and private-LAN HTTPS support.
- Firebase publisher source retained for rollback but not invoked by this monitor.

**Automatic deduction from real Windows print jobs is unavailable.** Enabling Automatic Paper Estimate saves a preference, not working automatic counting. Spooler pages, requested copies/duplex and completion flags do not reliably establish physical sheets. Unknown usage preserves stock and asks you to check the paper. Conditional positive accounting tests use synthetic receipts; they do not validate Canon/Epson hardware capability.

## Install and run

1. Download the Windows x64 ZIP from this repository's GitHub Releases and extract the **entire** folder. Do not run directly inside the ZIP. Use a supported Windows x64 installation with the printers already installed in Windows. Validation was performed on Windows 10 x64; other OS/driver combinations require testing. Follow your OS vendor's support requirements.
2. Close any older monitor sharing the same Windows user profile. There is no installer, service, auto-start or firewall modification.
3. Right-click Start-Monitor.ps1 and run in PowerShell, or open PowerShell in the extracted folder and run `& .\Start-Monitor.ps1`. If execution policy blocks scripts, use your approved signing/policy process; do not bypass security policy merely to launch it.
4. Supply an API token of at least 32 UTF-8 bytes through the launcher's hidden prompt. Use a high-entropy token generated/stored in your password manager. It is not printed or written to configuration. Default API binding is localhost `http://127.0.0.1:5050`.
5. Direct executable launch is also supported with the environment settings below. If server settings are missing/invalid, the UI continues monitoring and reports the API stopped; it does not fall back to an unauthenticated listener.

The executable is not code-signed. Windows SmartScreen/reputation warnings may occur; verify the release SHA-256 before use and follow your organization's software approval process. Do not disable Windows security controls.

## Paper and job views

Manage Paper saves the **total currently loaded**, not an amount to add. Canon and Epson settings persist independently. Remaining count is an estimate. LOW applies when positive remaining stock is at or below the threshold; zero means PAPER EMPTY. Notifications can be suppressed by Windows notification settings/Do Not Disturb. Use Test notification without printing or changing inventory.

Current Printing refreshes about every three seconds. Print History retains up to 500 observed jobs for 30 days. Short jobs can be missed. Document names, contents and users are not collected. Settings are requested metadata, not verified output; missing settings say Unknown. A vanished job has unknown outcome; spooler completion is not a physical completion receipt. No progress percentage is invented. Paper summaries read the same persisted inventory; there is no separate stock counter.

Local files live under `%LOCALAPPDATA%\SnapPrintMonitoringDesktop`: `paper-inventory.json` and `print-history.json`. Keep backups through your local data policy; files have normal user-profile filesystem protection and are not encrypted. Inventory/history files are not included in release packages. Do not run multiple instances against the same files. Nothing is silently reset during upgrade; close the old app and extract the new version separately. Retain user data when uninstalling the extracted app folder.

## Authentication and LAN HTTPS

Every API request requires `Authorization: Bearer <token>`. Endpoints are read-only `GET /api/health` and `GET /api/printers`; no print/management commands are exposed. API responses include device/queue diagnostics, so share credentials only with trusted monitoring clients.

| Setting | Purpose |
|---|---|
| SNAPMON_TOKEN | Required random bearer secret, at least 32 UTF-8 bytes |
| SNAPMON_BIND_IP | Default 127.0.0.1; LAN must be an explicit local private IPv4 address |
| SNAPMON_PORT | Default 5050 |
| SNAPMON_ALLOWED_CLIENTS | LAN: comma-separated exact private IPv4 client allowlist |
| SNAPMON_CERT_PATH | LAN: protected HTTPS PFX path |
| SNAPMON_CERT_PASSWORD | PFX password, supplied privately |

For the launcher, copy `monitor-config.example.psd1` to a local file such as `monitor-config.psd1`, edit only nonsecret address/port/allowlist/PFX-path values, then run `& .\Start-Monitor.ps1 -ConfigurationPath .\monitor-config.psd1`. Keep the PFX outside the app/repository folder with restricted private-key access. The certificate must match the LAN IP in its IP Subject Alternative Name; clients must validate both trust and name. Router internet access is unnecessary. Shared kiosk Wi-Fi is not inherently trusted: retain HTTPS, authentication and the explicit client allowlist. No wildcard/public listener, router port forwarding or broad firewall rule is required or supplied.

See [LAN_HTTPS.md](LAN_HTTPS.md) for certificate setup and narrowly scoped Private-profile firewall guidance. Certificate trust installation and firewall changes require separate operator approval; the app/package does neither. Do not use `-SkipCertificateCheck`, curl `-k`, disabled authentication or relaxed firewall rules as connectivity fixes. Local API examples using an already configured token (do not print it):

```powershell
$headers = @{ Authorization = 'Bearer ' + $env:SNAPMON_TOKEN }
Invoke-RestMethod http://127.0.0.1:5050/api/health -Headers $headers
Invoke-RestMethod http://127.0.0.1:5050/api/printers -Headers $headers
```

## Build, tests and limits

Source builds require the .NET 10 SDK on Windows. Run `dotnet build SnapPrintMonitoringDesktop.slnx -c Release`, then `dotnet SnapPrintMonitoringDesktop.Tests\bin\Release\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll`. Add `--ui-checks` for offscreen UI validation. Tests use synthetic jobs and temporary files; they do not print. See [RELEASE_NOTES.md](RELEASE_NOTES.md) for release validation and [JOB_SETTINGS.md](JOB_SETTINGS.md), [PRINT_HISTORY.md](PRINT_HISTORY.md) and [OPTIONAL_ESTIMATE.md](OPTIONAL_ESTIMATE.md) for evidence boundaries.

Physical ONLINE/OFFLINE and paper consumption remain dependent on device/driver evidence; USB presence is not ONLINE. No physical printer/Android interoperability is claimed by automated tests. Android integration is planned next. This release contains no Android, kiosk, Arduino/payment changes or remote internet monitoring.
