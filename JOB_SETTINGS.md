# Observed job settings and shared paper summaries

## Findings on this PC

Read-only inspection of existing local history found:

| Printer | Observed PagesPrinted / TotalPages | Copies requested | Duplex requested |
|---|---|---|---|
| Canon MG3000 series | 5 / 1 and 2 / 1 | 1 | SIMPLEX_REQUESTED |
| EPSON L3210 Series | 0 / 1 (printed value shown Unknown) | 1 | SIMPLEX_REQUESTED |

These are saved prior observations, not physical tests performed in this request. Live sampling during this request found both job queries AVAILABLE_FIRST_256_JOBS with zero active jobs. Therefore the newly added paper/N-up/scale fields have not yet been captured from actual Canon/Epson jobs. PrintService Operational logging is disabled and was left unchanged. We cannot infer copy multiplication or driver page semantics from the mismatched counters.

## Exact fields collected

The supported read-only native EnumJobs JOB_INFO_2 collector provides JobId, Submitted UTC, Status, TotalPages, PagesPrinted and pDevMode. Public DEVMODEW data is parsed with dmSize, dmFields and buffer-bound checks:

- dmCopies and dmDuplex remain requested settings (existing capture).
- dmPaperSize as DMPAPER code with a known PaperKind label where available.
- dmPaperWidth and dmPaperLength in tenths of a millimetre, only when their flags are declared.
- dmScale requested percentage.
- dmNup handling mode, only when declared and the public structure is long enough.
- dmSize, dmDriverExtra length and dmFields raw mask in diagnostics. Private driver bytes are neither parsed nor stored.

Missing fields use Unknown. Zero page counters are shown Unknown because unavailable delimiting data cannot be distinguished reliably. History preserves highest observed page counters; columns explicitly say highest. Document page count remains Unknown, separate from reported job totals. Copies are not assumed to be included or excluded from either counter.

DEVMODE dmNup is a handler choice, not a numeric pages-per-sheet field: system handles N-up, or system does not handle it (the application can still do so). Actual PagesPerSheet remains Unknown. Print Schema/PrintTicket has a PagesPerSheet setting, but a queue's default ticket is not the actual ticket used by an arbitrary job. No default-ticket substitution, spool-content inspection, private-driver reverse engineering or printing/filter installation was performed. JOB_INFO_4 adds large spool-size support rather than resolving document/N-up/completion semantics. PrintService completion events would not establish physical-sheet totals by themselves.

Primary references:
- https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-2
- https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-devmodew
- https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-4
- https://learn.microsoft.com/en-us/dotnet/api/system.printing.printticket.pagespersheet?view=windowsdesktop-10.0

## Interface and persistence

Current Printing and Print History show copies, requested duplex, requested paper, N-up handling and unknown document pages in additional table columns; horizontal scrolling accommodates narrow windows. Full numeric DEVMODE diagnostics remain separate from the main dashboard. Job metadata persists with the existing local, privacy-preserving, bounded history. Names/contents/users are not collected. Older records lacking new metadata remain Unknown.

Both tabs show compact per-printer summaries from the existing PaperInventory snapshots. All printers shows separate Canon and Epson summaries, never combined stock:

- Sheets Loaded: last saved operator total; older files show Unknown until the next save, rather than reconstructing a loaded total from current remaining stock.
- Low Paper Alert At: existing threshold.
- Paper Remaining (Estimated): existing remaining count.
- Total Estimated Sheets Accounted For: cumulative accepted automatic usage in the existing inventory document, not a second stock counter. It is not reset by replacing/refilling stock. Manually recorded usage is not included in this automatic total. Older files with prior accepted receipt hashes but no reconstructable totals show Unknown.
- Paper Status: NORMAL / LOW PAPER / PAPER EMPTY based on the remaining estimate; uninitialized stock is Unknown.
- Accounting confidence: Estimated / Needs Checking, with Paper count needs checking when uncertain.

Accepted conditional adapter receipts update remaining stock, cumulative accounted total, duplicate-protection ledgers and alert suppression in one existing atomic inventory save. History links a committed receipt only with matching printer/job/submission timestamp and persisted receipt hash, preventing reused job IDs from inheriting old consumption. It displays accounted sheets as an unverified software estimate, never physical verification. Only the last accepted receipt in a snapshot can be linked this way; historical receipts lacking submission identity or jobs missed between polls remain Unknown. This is conservative and does not reconstruct invented accounting records.

Production Windows spooler deductions remain unavailable. Settings do not supply validated layout/page semantics or physical completion. Unknown jobs preserve remaining stock and accounted total, mark checking needed and do not emit a false low-paper notification. Existing guarded receipt accounting is exercised with synthetic adapters only. Existing per-printer LOW/EMPTY notifications still apply to saved operator changes and eligible conditional receipts, with persistent duplicate suppression/rearm. No inventory transaction controls were added to Manage Paper.

## Files changed

- SnapPrintMonitoringDesktop/RequestedPrintSettings.cs: bounded public metadata model and diagnostic descriptions.
- SnapPrintMonitoringDesktop/PrinterSignals.cs: collect public DEVMODE fields and add raw diagnostics.
- SnapPrintMonitoringDesktop/PrintHistory.cs: metadata persistence and committed receipt linkage.
- SnapPrintMonitoringDesktop/PrintActivityView.cs: settings columns and filtered shared-inventory summaries.
- SnapPrintMonitoringDesktop/PrinterDashboard.cs: pass persisted inventory snapshots to both job views.
- SnapPrintMonitoringDesktop/PaperInventory.cs: persist last loaded amount/cumulative automatic total with validation and conservative legacy migration.
- SnapPrintMonitoringDesktop/AutomaticPaperUsage.cs: same-transaction total updates and submitted identity on accepted receipts.
- SnapPrintMonitoringDesktop.Tests/PrintSettingsChecks.cs: public DEVMODE and conflicting counters tests.
- Existing AutomaticUsageChecks, PaperSettingsChecks, PrintHistoryChecks, DashboardChecks, Program and test project: synchronization, parser/persistence/filter tests.
- JOB_SETTINGS.md: report and manual instructions.

## Validation performed

Build succeeded: zero warnings/errors. 225 automated checks passed, including 11 public-setting checks, 40 conditional-accounting checks, history restart/deduplication, unknown/error jobs, paper settings, threshold notifications and authenticated API checks. 35 offscreen UI checks passed. Current/history renders were inspected. 10 LAN HTTPS regression checks passed on a temporary unused port with protected temporary PFX files, authentication, certificate/IP validation and shutdown; no firewall/trust-store changes. No physical printing, newly captured hardware settings, actual sheet usage, Android connectivity, live notification delivery or high-DPI display testing was performed.

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=C:\CODE\snapprint-monitor-desktop\diagnostics\automatic-usage-build\
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --ui-checks
# Optional read-only live signal capture:
dotnet .\diagnostics\automatic-usage-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --capture-signals | Set-Content .\diagnostics\job-settings-signals.json
```

## Safe manual tests

1. Close the older monitor yourself. Launch diagnostics/automatic-usage-build/Debug/net10.0-windows/SnapPrintMonitoringDesktop.exe with existing secure API configuration. Do not run two instances sharing inventory/history files.
2. Save each printer's actual Sheets Loaded and Low Paper Alert At. Check All printers shows independent summaries; selecting Canon/Epson shows only that printer. Older Sheets Loaded may be Unknown until saved. No stock is silently reset.
3. Only if you choose to print, use harmless documents and record printer, application, requested settings, job time and actual sheets separately. Test one page/one copy; one page/multiple copies; multiple pages; supported duplex/manual duplex and N-up. The app never initiates printing. Do not force unsupported driver modes or waste paper solely to exercise diagnostics.
4. Watch Current Printing and inspect Diagnostics while the job is present. Compare dmCopies/dmDuplex/paper/dmNup with selected settings; do not infer document pages or sheets from mismatches. Three-second polling may miss short jobs or transient metadata. Missing settings must say Unknown.
5. Inspect History after disappearance and after restart. Metadata should persist; disappearance means unknown outcome. A reported 5/1 pair must remain a 5/1 pair, without percentage or sheet deduction. Unknown usage must leave stock/accounted total unchanged and show checking needed.
6. If cancelling a normal job yourself, note any actual fed paper; neither cancellation nor an error proves zero usage. Do not induce jams, modify drivers, restart spooler or enable event logs for this test.
7. Verify remaining sheets physically and save the current total if necessary. A real count at/below threshold should trigger existing LOW once; zero should trigger EMPTY once; replenishment above threshold rearms. Use Test notification to verify Windows delivery without printing or inventory changes.

Only this desktop repository was modified. No commit, push, deploy or other-project changes.
