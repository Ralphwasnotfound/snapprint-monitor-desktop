> Current optional-mode behavior: see OPTIONAL_ESTIMATE.md. The saved opt-in preference does not bypass missing spooler evidence.

# Two-field paper settings

This revision supersedes the previous three-operation interface.

Each printer's Paper Settings window has exactly two numeric fields, Sheets Loaded and Low Paper Alert At, and one Save Paper Settings button. Saving replaces the current estimate and threshold together through the existing revision-checked, transactional inventory service. Values are bounded to 0–100,000. Canon and Epson settings persist independently. No operation selector, add/subtract controls or reconciliation terminology is presented in the settings window. Existing internal accounting actions remain for compatibility and tests.

The dashboard shows Paper Remaining (Estimated), LOW PAPER for positive stock at or below threshold, and PAPER EMPTY at zero. It shows “Automatic paper counting unavailable for this printer.” Unknown printing asks the operator to check the paper amount. Saved changes use the existing persisted notification policy: threshold/empty crossings notify, repeated refreshes/saves do not spam, and a genuine increase above threshold rearms alerts. Test notification remains available.

## Automatic tracking

The existing supported native EnumJobs JOB_INFO_2 collector records observed Windows job TotalPages, PagesPrinted, submission identity, status flags and requested DEVMODE copies/duplex in diagnostic evidence. These values are available through existing monitoring snapshots; polling can miss short jobs, so this is not a durable history of every print job. A page count is not automatically a physical-sheet count. Copies, page ranges/layout, duplex, partial failures and job disappearance remain uncertain. No spooler-only deductions or production counting mode were enabled. Duplicate-job protection and the conditional validated-adapter pipeline are unchanged; positive automatic accounting tests use synthetic receipts only.

See AUTOMATIC_PAPER_USAGE.md for supported Windows-source limitations. Prior investigation found a cumulative feed counter documented in the Epson L3210 Windows utility, but no supported automated retrieval interface was established or validated. That remains a possible driver-specific follow-up, not an enabled capability. Canon has no validated physical-sheet telemetry in this application. No new physical printing or device telemetry validation was performed in this request.

## Files changed in this revision

- SnapPrintMonitoringDesktop/InventoryDialog.cs: replace selector workflow with two fields and one button; reuse SetCountedTotal internally.
- SnapPrintMonitoringDesktop/PrinterDashboard.cs: Paper Settings access, PAPER EMPTY and exact automatic-unavailable wording.
- SnapPrintMonitoringDesktop.Tests/DashboardChecks.cs: assert two fields/one button, exact labels, replacement semantics, bounds and responsive layout.
- SnapPrintMonitoringDesktop.Tests/PaperSettingsChecks.cs: independent saves, inclusive threshold, LOW/EMPTY notifications, refill rearm, persistence and restart suppression.
- SnapPrintMonitoringDesktop.Tests/Program.cs: run settings checks.
- SIMPLE_PAPER_UI.md: updated report.

## Validation

Build passed with zero warnings/errors. Passed 10 settings checks, 14 legacy internal input checks, 36 automatic-accounting checks, 36 inventory checks, 29 notification checks and 46 status/API checks (171 total). Passed 24 offscreen UI checks. Rendered settings and minimum dashboard images were visually inspected. Live read-only discovery found seven Windows queues. Synthetic tests do not print or send actual Windows notification balloons. No live notification delivery, physical consumption, Android connectivity or high-DPI screen testing was performed.

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=C:\CODE\snapprint-monitor-desktop\diagnostics\automatic-usage-build\
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --ui-checks
# Optional read-only signal snapshot; does not change inventory or print:
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --capture-signals | Set-Content .\diagnostics\paper-settings-signals.json
```

## Manual checks

1. Close the older monitor yourself and launch diagnostics/automatic-usage-build/Debug/net10.0-windows/SnapPrintMonitoringDesktop.exe with your existing secure API settings. Do not run two monitors against the same inventory.
2. Open either printer's Paper Settings. Confirm exactly two fields and one save button. Sheets Loaded means the total currently loaded, not an amount to add.
3. Use actual loaded counts in production. For an isolated example, save 10 sheets with threshold 5, then save 5: expect LOW PAPER once. Save 4: no repeated LOW. Save 0: expect PAPER EMPTY. Save 10: alerts rearm. Canon changes must not change Epson.
4. Restart: saved counts/thresholds remain and alerts do not repeat merely because of restart. Use Test notification to check Windows delivery without inventory changes; Windows notification settings may suppress balloons.
5. Optional user-initiated printing: inspect Diagnostics before/during/after for raw reported pages and flags. Do not expect automatic deduction. If sheets used are uncertain, check actual remaining paper and save that total. No automatic printing is needed to validate this UI.

Authentication, HTTPS, printer detection, inventory format, notification mechanisms and Firebase-disabled behavior are preserved. No Android/kiosk/Arduino changes, firewall/trust-store changes, commits, pushes or deployment.
