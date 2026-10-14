# Optional Automatic Paper Estimate — guarded fallback

## Outcome

The per-printer checkbox is implemented, defaults OFF and persists atomically with Sheets Loaded and Low Paper Alert At. The simple Manage Paper dialog explains that this is a preference, not evidence of supported accounting. Enabling it currently does NOT enable spooler-based deductions on Canon MG3000 series or EPSON L3210 Series. This implements the request's fallback when a safe sheet-count heuristic cannot be established. It is not a working automatic sheet calculator for these drivers.

Why: even with positive matching TotalPages/PagesPrinted, dmCopies and dmDuplex, the collector cannot establish whether reported pages already include copies, selected output ranges, driver-side N-up/booklet scaling, manual duplex passes or partially fed output. Multiplying generic Windows counters would invent semantics, not merely estimate uncertain completion. Opting into uncertain completion does not supply missing sheet-count information. Therefore no mathematical page-to-sheet formula or queue-disappearance heuristic was activated. The examples 4 simplex=4, duplex=2 and two copies=8 are not asserted from generic counters. The existing independent validated-adapter pipeline remains the only conditional deduction path; its positive tests are synthetic.

## Implementation

- Settings: AutomaticPaperEstimateRequested is distinct from AutomaticDeductionEnabled. Existing stored files default false; existing internal adjustments that omit the preference retain it. Legacy API fields and physical-verification flags remain conservative. The API additionally serializes the requested preference, read-only.
- History captures the preference when a job is first observed; opting in halfway through a job does not retroactively authorize it. New observations persist their accounting decision with local history metadata. Document names/contents remain uncollected.
- Decisions explicitly report mode OFF, cancelled/failed/interrupted job, missing identity/continuity, unknown outcome, missing pages/settings, partial/inconsistent pages or unresolved page/copy/layout semantics. Estimated sheets stay Unknown; InventoryDeducted and PhysicalCompletionVerified stay false for these spooler observations.
- Current Printing and Print History use the per-job decision instead of generic Needs confirmation. No progress percentage is invented. The dashboard shows Auto estimate requested — insufficient job data when requested, or Automatic Paper Estimate: off otherwise. The healthy paper state also displays the configured threshold. Details remain in History/Diagnostics.
- Existing count/receipt/job-ledger transactions, negative-stock and over-stock discrepancy safeguards, notifications, restart protection and Canon/Epson isolation are unchanged. No new inventory deductions means no new automatic LOW/EMPTY event from real spooler jobs. Saved manual amounts still trigger existing LOW/EMPTY transitions and refill rearm normally.

## Windows evidence actually checked

Read-only live sampling found Canon MG3000 series and EPSON L3210 Series, both with AVAILABLE_FIRST_256_JOBS and zero jobs. Thus no real submitted timestamp, page count, copies or duplex values were available to validate in this session. The supported existing collector is EnumJobs JOB_INFO_2 with bounded DEVMODE parsing. WMI and PrintQueue remain supplemental. Microsoft-Windows-PrintService/Operational was disabled; it was not enabled. No printer-specific telemetry or physical consumption was newly validated.

Microsoft's JOB_INFO_2 documents spooler completion and page-counter limitations: https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-2
Requested settings structure: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-devmodew
See AUTOMATIC_PAPER_USAGE.md and PRINT_HISTORY.md for polling, layout, model-specific telemetry and privacy limitations. A future driver-specific study must establish page/copy/layout semantics or a supported sheet counter before implementing a useful opt-in arithmetic heuristic. Ordinary completion flags alone cannot resolve this blocker.

## Files changed

- SnapPrintMonitoringDesktop/PaperInventory.cs: requested-mode preference and transactional save compatibility.
- SnapPrintMonitoringDesktop/InventoryDialog.cs: optional checkbox, uncertainty explanation and simple save.
- SnapPrintMonitoringDesktop/PrinterDashboard.cs: Manage Paper, requested-mode status and healthy threshold text.
- SnapPrintMonitoringDesktop/SpoolerEstimatePolicy.cs: conservative per-job eligibility/rejection reasons.
- SnapPrintMonitoringDesktop/PrintHistory.cs: job-local opt-in and persisted accounting decisions.
- SnapPrintMonitoringDesktop/PrintActivityView.cs: actual decision text.
- SnapPrintMonitoringDesktop/Form1.cs: provide attached inventory settings to history observation.
- SnapPrintMonitoringDesktop.Tests/SpoolerEstimateChecks.cs: 15 eligibility/rejection checks.
- SnapPrintMonitoringDesktop.Tests/PaperSettingsChecks.cs, PrintHistoryChecks.cs, DashboardChecks.cs: preference persistence/isolation, opt-out, job identity reuse, saved decisions and checkbox UI.
- SnapPrintMonitoringDesktop.Tests/Program.cs and .csproj: test inclusion.
- OPTIONAL_ESTIMATE.md and documentation pointers.

## Validation

Build passed with zero warnings/errors. Passed 208 automated checks: 15 policy, 17 history, 15 settings, 14 legacy input, 36 conditional accounting, 36 inventory, 29 notifications and 46 status/API. Passed 31 offscreen UI checks. Passed 10 LAN HTTPS checks with temporary protected PFX on an unused port: load/start, unauthenticated/wrong-token rejection, authenticated health/printers, inventory JSON, default trust rejection, wrong IP SAN rejection and disposal. No certificate-store or firewall changes. The Manage Paper render was inspected. No physical printing, Windows balloon delivery or Android connectivity was tested.

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=C:\CODE\snapprint-monitor-desktop\diagnostics\automatic-usage-build\
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --ui-checks
```

## Exact Epson manual test

1. Close the older monitor yourself; launch diagnostics/automatic-usage-build/Debug/net10.0-windows/SnapPrintMonitoringDesktop.exe with existing secure API environment. Do not run simultaneous instances sharing local files.
2. In Epson > Manage Paper, enter the actual total loaded. For the requested example, physically load five sheets, enter Sheets Loaded 5 and Low Paper Alert At 2. Leave automatic preference OFF first and save.
3. Reopen: confirm saved fields and unchecked preference. Check Automatically estimate paper used from print jobs, read the insufficient-data explanation and save. Reopen/restart to verify persistence; Canon should remain unchanged.
4. If desired, manually print a harmless four-page simplex one-copy document. The app never prints automatically. Watch Current Printing and then Print History. Short jobs may be missed by three-second polling. Reported pages may be Unknown. With current evidence, estimated sheets must remain Unknown and inventory must remain 5; the accounting reason explains why. No automatic LOW notification is expected from this rejected job.
5. If four physical sheets were actually consumed, verify the one remaining sheet and save Sheets Loaded 1. Expect LOW PAPER once. Re-save 1: no duplicate notification. Set 0 only when actually empty: expect PAPER EMPTY. Replenish above threshold to rearm. Test notification can verify Windows delivery without printing/count changes.
6. Uncheck the optional preference and save: future jobs should record mode OFF. Saving always replaces the current amount; it never adds to it. Restart must preserve counts/preferences and not repeat previous alerts.
7. Further driver study should record raw pages/copies/duplex before/during/after operator-controlled copies, selected ranges and supported duplex cases, and compare actual sheets. Do not infer semantics from a single four-page success. Do not change drivers, enable event logs, restart spooler or induce jams merely for this test.

Only this desktop repository was modified. No commit, push, deploy, automatic physical printing or other-project changes.
