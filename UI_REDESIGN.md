# Two-printer dashboard redesign

The main WinForms dashboard displays Canon MG3000 series and EPSON L3210 Series in two equal-width cards. The presentation filter uses exact case-insensitive names; WindowsPrinterMonitor, SnapshotStore and the authenticated API still retain all installed queues. There is no classifier, authentication, HTTPS or Firebase publishing behavior change.

Each card separates physical status (UNKNOWN), USB presence and explicitly Windows-reported queue state. USB_PRESENT is never displayed as ONLINE. A missing queue shows Not found in Windows and clears USB/queue values; discovery failure shows Discovery unavailable rather than implying unplugging. Physical status remains supplied by the existing conservative PhysicalStatus property.

The light dashboard uses Segoe UI, white cards, consistent margins and simple vector printer icons. Status words accompany colors. A compact server indicator distinguishes starting/running/stopped and displays the configured address while running. Automatic background polling retains its existing three-second delay between queries. The footer shows the sample timestamp. Diagnostics opens a separate, resizable window and updates as readings arrive; it includes all queues, raw technical details, safe server errors and discovery errors. No credentials are added to the UI.

## Files changed

- Form1.Designer.cs: dashboard host, DPI-based scaling, initial and minimum window sizes.
- Form1.cs: forwards snapshots and server state to the dashboard; existing polling/server lifecycle retained.
- PrinterDashboard.cs: two cards, presentation filter, server indicator, missing/error states, icons and separate diagnostics window.
- Tests/DashboardChecks.cs: UI rendering, resize/bounds, filter isolation, status separation, missing queues, discovery failure and diagnostics checks.
- Tests/Program.cs and Tests/csproj: UI-check mode and linked dashboard source using built-in WinForms, without a new package.

## Validation

Build passed with zero warnings/errors. All 43 existing status/API tests and 13 UI checks passed. UI checks rendered offscreen at the default size, minimum client size (884 x 651) and a wider size (1280 x 820), plus missing/discovery-error states. The minimum layout was visually inspected and corrected to avoid clipped status labels. The separate diagnostics window was opened programmatically, verified to include virtual printers and tested for live refresh/reuse. No printer commands were sent.

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project .\SnapPrintMonitoringDesktop.Tests --no-build
dotnet run --project .\SnapPrintMonitoringDesktop.Tests --no-build -- --ui-checks
```

Generated PNG previews under diagnostics use **synthetic fixtures**, including Epson USB-not-present/offline. They do not describe the hardware's current state. The preview is not evidence of live printer/UI testing.

## Limitations / manual check

The main window enforces a 900 x 690 minimum outer size to keep both cards readable side by side. Smaller windows are not supported. High-DPI/multiple-monitor scaling, screen-reader behavior and prolonged interactive use were not manually verified. At the deployment PC, open the app using its existing environment configuration, resize it, open/close Diagnostics, and confirm current readings update. Existing secure HTTPS launch instructions remain in LAN_HTTPS.md. Renamed queues stay in diagnostics/API but will appear missing on the exact-name cards until the dashboard names are deliberately updated.

No kiosk/Android edits, firewall/trust-store changes, commits, pushes or deployment were performed.

Paper inventory follow-up: [PAPER_INVENTORY.md](PAPER_INVENTORY.md) documents per-printer operator estimates, reconciliation controls and updated UI/API tests.
