> Current optional-mode behavior: see OPTIONAL_ESTIMATE.md. The saved opt-in preference does not bypass missing spooler evidence.

# Current Printing and Print History

## Delivered interface

Three tabs organize the desktop app:
1. PRINTER STATUS / PAPER SETTINGS: two physical printer cards; existing two-field Sheets Loaded / Low Paper Alert At window and Save Paper Settings button.
2. CURRENT PRINTING: automatically refreshed currently observed jobs, printer filter, job status, reported printed pages and total pages. Progress always says unavailable because current spooler counters have not been validated as reliable physical progress. No invented percentages.
3. PRINT HISTORY: local persistent observed jobs with date/time, printer, privacy placeholder for document, highest observed reported-page values, unknown estimated sheets, last known status and accounting confirmation status. Filter All / Canon / Epson. Long table values wrap.

Document names and contents are never collected or stored. This deliberately keeps document-name storage disabled for all users; no opt-in collection mode was added. The Document column clearly says Not collected (privacy). No usernames are stored. History is local only and is not added to HTTP API responses.

## Tracking and accounting evidence

The existing supported native EnumJobs JOB_INFO_2 snapshot source supplies job ID, submission timestamp, reported TotalPages/PagesPrinted, raw flags and requested copies/duplex. Existing WMI/PrintQueue evidence remains supplemental. PrintService events and spooler change notifications were considered; none establishes physical sheets by itself. No event-log setting or driver was changed. Polling stays every three seconds and can miss jobs that enter/leave between observations. This is observed-job history, not a complete print audit.

Positive Windows page counters can be recorded as reported metadata, not independently confirmed printed pages. Zero is displayed Unknown because unavailable and genuine zero cannot be reliably distinguished here. The journal preserves the highest observed page values rather than erasing them when a later driver snapshot reports zero. Requested copies and duplex are stored but never used for unsupported sheet arithmetic; selected page ranges/N-up remain unresolved.

No job is labeled physically successfully completed from PRINTED/COMPLETE flags. These produce Spooler completion reported — physical outcome unknown. Queue disappearance produces Unknown outcome. Error/cancellation evidence is retained even when flags later clear. Gaps over 15 seconds, clock reversal, unavailable queries and interrupted active jobs are marked. On restart, saved active jobs become Unknown outcome — application restarted. Timestamped job identities deduplicate across restarts. Jobs lacking submission time receive session-scoped identities, avoiding false cross-restart merges; these cannot provide complete duplicate identification if drivers omit time or reuse IDs unseen between polls.

No new automatic inventory deductions are enabled. Physical completion verified remains false and estimated sheets remain unknown. The total sheets accounted for by this journal is explicitly zero, not a lifetime inventory total. The existing validated-adapter accounting engine, persistent deduplication ledger and notifications are preserved separately. A future real adapter would need an explicitly validated receipt linkage before history could display its sheet totals. Do not interpret unknown usage as zero physical consumption.

Windows documentation: https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-2
Prior device-specific findings and accounting safeguards: AUTOMATIC_PAPER_USAGE.md and SIMPLE_PAPER_UI.md.

## Persistence and privacy

File: %LOCALAPPDATA%\SnapPrintMonitoringDesktop\print-history.json, separate from paper-inventory.json. Only Canon/Epson metadata is retained. Maximum 500 observed jobs and 30 days, pruned on polling (including failed-discovery snapshots). File reads are bounded to 2 MiB. An exclusive lock prevents concurrent writers; temp file flush plus atomic replacement avoids partial writes. Invalid/inaccessible history is preserved with an error shown in the job views, rather than silently reset. On save failure, old persisted history remains; the UI warns that observations may be incomplete. Retention does not prune or reset the independent inventory duplicate-protection ledger. Metadata uses the Windows user profile's normal filesystem permissions; it is not encrypted.

## Files changed for this request

- SnapPrintMonitoringDesktop/PrintHistory.cs: bounded journal, observation identity, statuses, gaps, privacy and atomic persistence.
- SnapPrintMonitoringDesktop/PrintActivityView.cs: current/history tables and independent printer filters.
- SnapPrintMonitoringDesktop/PrinterDashboard.cs: separate tabs, job view updates and spacing adjustments.
- SnapPrintMonitoringDesktop/Form1.cs: initialize/observe/dispose history off the UI thread during existing polling; refresh UI after observations.
- SnapPrintMonitoringDesktop.Tests/PrintHistoryChecks.cs: synthetic history tests.
- SnapPrintMonitoringDesktop.Tests/DashboardChecks.cs: tabs, tables, filtering and responsive UI checks.
- SnapPrintMonitoringDesktop.Tests/Program.cs and .csproj: include new checks and linked production classes.
- PRINT_HISTORY.md: this report.

## Validation performed

Build: zero warnings/errors. 186 automated checks passed: 15 history, 10 settings, 14 internal input, 36 conditional automatic usage, 36 inventory, 29 notification and 46 status/API checks (including unauthorized rejection). 29 offscreen UI checks passed. Current/history/settings/minimum dashboard renders were generated; current/history renders were visually inspected. Live read-only discovery found seven queues. No physical printing, physical completion validation, Android connectivity or actual Windows balloon delivery was tested. HTTPS implementation/configuration is unchanged; TLS-specific tests were not rerun in this request.

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=C:\CODE\snapprint-monitor-desktop\diagnostics\automatic-usage-build\
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --ui-checks
```

## Manual testing

1. Close the older running monitor yourself; launch diagnostics/automatic-usage-build/Debug/net10.0-windows/SnapPrintMonitoringDesktop.exe using the existing secure API environment. Do not run two monitors sharing local inventory/history files.
2. Verify both printer cards and the unchanged two-field Paper Settings window. Use real loaded counts; saving replaces the estimate. Use Test notification for a safe Windows notification check.
3. Open CURRENT PRINTING. If you choose, manually print a nonprivate test document to Canon. Switch to Epson for a separate operator-initiated job. No physical printing is started by the app or tests. Very short jobs may be missed; a longer normal document may be easier to observe, but do not waste paper just to satisfy the UI.
4. Observe reported pages/total if the driver supplies them. Unknown and page progress unavailable are expected. No percentage or physical sheet deduction should appear. Record actual sheets separately if studying driver accuracy.
5. Open PRINT HISTORY; compare All, Canon and Epson filters. Confirm document names are not collected. If a job disappears without a reliable receipt, expect Unknown outcome. If Windows completion flags were sampled, expect Spooler completion reported — physical outcome unknown, never verified success.
6. If cancelling a normal job yourself, note any paper already fed; Windows cancellation does not prove zero consumption. Expect cancellation/error evidence if polling observes it. Do not induce jams, change drivers, pause queues or restart spooler for this test.
7. Restart the monitor. History should remain, saved active observations should become unknown interrupted outcomes, and inventory should not deduct again. LOW/EMPTY notifications remain governed by saved paper settings and the existing suppression/rearm policy.
8. For unknown consumption, count remaining sheets and save Sheets Loaded with the actual total. No add/subtract or reconciliation operation selector is exposed.

Only this desktop repository was modified. No commit, push, deploy, firewall/trust-store change, driver change or automatic physical printing.
