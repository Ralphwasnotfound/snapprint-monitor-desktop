> Current optional-mode behavior: see OPTIONAL_ESTIMATE.md. The saved opt-in preference does not bypass missing spooler evidence.

# Safe automatic paper accounting

## Result and reliability boundary

Generic Windows printing data is insufficient to enable automatic physical-sheet deduction for Canon MG3000 series or EPSON L3210 Series. The production Windows source therefore deliberately remains disabled. Real printing preserves the estimate and records a per-job reason for reconciliation. No setting, imported event, API request, or checkbox can promote spooler counters into physical receipts.

A guarded deduction pipeline is implemented for a future independently validated device/driver completion adapter. Positive automated tests inject a synthetic adapter; they do not demonstrate actual printer capability. Automatically deducted inventory remains an unverified software estimate, never a verified physical paper count.

## Windows sources investigated

The selected read-only diagnostic source is native EnumJobs level 2 (JOB_INFO_2), alongside the existing WMI, PrintQueue and USB evidence. It now exposes submission identity, raw job flags, reported page counts and bounded DEVMODE requested copies/duplex values. It does not collect private document names or users.

Microsoft documents that PRINTED can mean data was sent to the printer when a monitor lacks TrueEndOfJob; COMPLETE does not establish physical output. TotalPages may be zero. DEVMODE describes requested settings, not confirmed sheets. Page ranges, N-up, copies handled inside a driver, duplex blank sides, jams and partial output prevent a universal pages-to-sheets calculation. WMI Win32_PrintJob repeats spooler counters rather than providing independent physical confirmation. PrintService Operational logging was inspected read-only and was disabled on this PC; it was not enabled. A completion event alone would not settle these physical/layout uncertainties.

Sources:
- https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-2
- https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-1
- https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-devmodew
- https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-printjob

## Conditional accounting safeguards

A future adapter must attest a positive completed physical-sheet total, including copies, selected output and duplex effects, with exact printer/device/job/submission identity. Requested settings are never multiplied into an invented total. The engine additionally requires an observed active start after an operator baseline, continuous monitoring, exact USBPRINT queue/device mapping, unchanged manual count generation, no observed error/cancellation/restart flags and valid receipt timing.

Unknown pages, cancelled/failed/partial jobs, disappearing jobs without receipts, ambiguous devices, monitoring gaps, missing starts and application interruptions cannot authorize deductions. Restart requires a new operator reconciliation before new automatic accounting. A receipt exceeding stock preserves the count and flags uncertainty instead of clamping to a misleading zero.

Count, receipt hash, job identity hash and notification suppression state are saved together atomically. Replayed receipts and alternative receipts for the same job cannot deduct twice, including after restart. Ledgers retain up to 4,096 accepted jobs per printer without pruning; accounting stops at capacity. The inventory file has a 4 MiB read bound. Storage failure emits no deduction notification. Manual adjustment/confirmed usage remain available; check the last automatic deduction before manually recording the same usage.

LOW and EMPTY transitions reuse the existing persisted alert policy and nonblocking Windows tray notifier. Automatic consumption never rearms refill alerts. Canon and Epson remain independent. Authenticated read-only API inventory fields include automatic availability, last automatic deduction, estimate source/confidence and the last usage decision. Existing authentication/HTTPS and Firebase-disabled defaults are preserved.

## Files changed for this task

- SnapPrintMonitoringDesktop/AutomaticPaperUsage.cs: guarded completion contract, default-disabled Windows source, identity/continuity validation and deduplication.
- SnapPrintMonitoringDesktop/PaperInventory.cs: persisted receipt ledger, transactional accounting, manual generation and estimate diagnostics.
- SnapPrintMonitoringDesktop/PrinterSignals.cs: JOB_INFO_2 metadata and bounded requested-layout parsing.
- SnapPrintMonitoringDesktop/Form1.cs: dispatch saved automatic accounting notifications on the UI thread.
- SnapPrintMonitoringDesktop/PrinterDashboard.cs: last automatic deduction and detailed usage decisions.
- SnapPrintMonitoringDesktop/InventoryDialog.cs: caution against manually counting already accounted usage.
- SnapPrintMonitoringDesktop.Tests/AutomaticUsageChecks.cs: synthetic accounting, restart, failure, layout and notification checks.
- SnapPrintMonitoringDesktop.Tests/Program.cs and project file: include and run the new checks.

Other existing uncommitted project changes predate this task. No commits were created.

## Validation performed

- .NET 10 solution build: succeeded, zero warnings/errors, alternate output to avoid replacing the running desktop application.
- 36 automatic usage checks: passed. Positive cases use synthetic physical receipts for single/multiple pages, copies, selected ranges and odd-page duplex; rejection cases include cancellation/failure, unknown counts, missing completion, gaps, ambiguous mapping, restart, stock overflow and failed persistence. Duplicate receipt/job protection, Canon/Epson isolation, LOW and EMPTY transitions passed.
- 36 existing inventory checks, 29 notification checks and 46 status/API checks: passed.
- 20 offscreen dashboard checks: passed.
- Live read-only discovery found seven Windows queues. This does not verify physical readiness.
- 10 LAN HTTPS checks passed on an unused temporary port using 192.168.254.120: encrypted certificate load, authenticated health/printers, unauthorized rejection, inventory JSON, certificate trust/IP validation and shutdown. No firewall or trust-store changes.
- No physical paper was printed automatically or verified during this task. No Android connectivity testing was performed. Prior manual print reports do not validate the new accounting adapter.

## Exact local commands

Run from PowerShell in this repository:

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=C:\CODE\snapprint-monitor-desktop\diagnostics\automatic-usage-build\
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --ui-checks
# Read-only snapshot; no printing or inventory adjustments:
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --capture-signals | Set-Content .\diagnostics\paper-usage-signals.json
```

## Manual Windows testing

1. Close the older monitor yourself before launching the rebuilt executable from diagnostics/automatic-usage-build/Debug/net10.0-windows. Keep your existing HTTPS/authentication launch configuration. Do not run two instances against the same inventory file.
2. With both queues idle, physically count and reconcile each printer separately. Set Epson to 10 and its threshold to 3 only if those counts reflect your actual loaded stock.
3. If desired, manually print a harmless one-page document. Record printer, click time, actual sheets, copies, page range, simplex/duplex and any error. Inspect diagnostics before/during/after. Expected production result: no automatic deduction; an explicit unverified completion/disappearance decision and reconciliation requirement.
4. Repeat manually with multiple pages, two copies, a selected page range and duplex only if the driver/printer supports it. A disappearing job or reported PRINTED state must not change inventory. Do not induce jams or failures for testing. If a job is cancelled, reconcile any sheets actually used.
5. Recount physical stock and use existing confirmed usage/reconciliation controls. An operator-confirmed 10-to-3 change should produce LOW, 3-to-2 should not repeat LOW, and 2-to-0 should produce EMPTY. A genuine refill above threshold rearms alerts. Do not record usage already deducted by any future validated adapter.
6. Restart and verify persisted counts, last decision and notification suppression. Restart should flag possible unseen activity without repeating alerts. Check Canon independently.

The next step is a controlled driver/device study seeking independently validated sheet-completion telemetry for these exact models. Ordinary Windows completion flags are insufficient to turn production accounting on safely. Until such evidence exists, manual confirmed usage and reconciliation are the supported workflow.
