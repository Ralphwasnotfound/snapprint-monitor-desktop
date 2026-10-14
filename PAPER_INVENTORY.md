# Sensorless paper inventory

## What is implemented

Canon MG3000 series and EPSON L3210 Series each have a locally persisted sheet estimate and warning threshold (default **3**). Unknown stock is displayed as unknown, never initialized to zero. The cards show the estimate, low/empty warning, last count-update time and confidence/reconciliation status. Warnings describe the **last estimate**, not a sensor reading. Physical printer readiness classification is unchanged.

Select **Adjust / refill** on the relevant card:

1. **Set counted total (reconcile):** enter the actual sheets currently in the printer, not the amount being added. With fresh idle job monitoring this establishes an OPERATOR_BASELINE. During active jobs or unavailable/stale monitoring the number is saved but remains reconciliation-required.
2. **Add loaded sheets (refill):** add a physically counted refill to the existing estimate. An unknown baseline must be initialized using a counted total first. Refill does not clear unresolved consumption.
3. **Record physically confirmed sheets used:** subtract actual sheets the operator has confirmed were consumed. Do not enter logical pages or requested copies. This preserves any unresolved reconciliation requirement; use a full recount to clear it.
4. **Change warning threshold only:** configure this printer's threshold without changing stock or pretending that a new count was performed.

Zero is a valid counted empty tray. Positive estimates at or below the threshold show LOW PAPER; zero shows EMPTY. A threshold of zero disables the positive low-stock warning but keeps the empty warning. Inputs/results are whole sheets in 0..100000. No negative stock or silent clamping. Adjustment dialogs detect stale inventory revisions, and repeated save requests cannot apply the same immediate refill twice.

## Consumption policy and confidence

**Automatic deduction is disabled.** Current Windows telemetry does not reliably establish physical sheet consumption. Every observed job (including retained/completed/failed jobs), printing indication, missing/unreadable queue/job data, monitoring interval over 15 seconds, backwards clock jump, or application restart can make an existing baseline require reconciliation. Count updates occur only through an operator action. Repeated polling of the same job changes no sheet count and does not repeatedly revise identical reconciliation state. A job disappearing never clears uncertainty.

OPERATOR_BASELINE means an operator entered a count while the monitored queue appeared idle. It does not mean real-time synchronization with physical stock. Confidence is always OPERATOR_ESTIMATE_UNVERIFIED for initialized stock; physicalCountVerified=false. A short job can start/finish between polls, the app can be closed, and a printer can consume paper through standalone copying or another unobserved path. These limitations prevent a continuously accurate sensorless counter. Recount at shift changes and after uncertain printing, jams, canceled/partial jobs or manual tray changes.

**Copies, page ranges, multipage and duplex:** the current JOB_INFO_1 counters do not describe all of these choices, and reported pages are not necessarily physical sheets. No copies=1, simplex, all-pages, or one-page-per-sheet defaults are invented. Operator-confirmed physical usage/recounts work regardless of those settings. No planned-sheet formula is used as actual consumption. For example, when independently known, two copies of three selected logical pages in duplex could request 2 × ceil(3/2)=4 sheets when each copy starts on a new sheet before considering N-up, separator pages, partial output or failures; blindly dividing aggregate page counts could give the wrong answer. A requested-sheet plan would still not prove actual consumption.

## Windows source investigation

- Existing EnumJobs/JOB_INFO_1 provides numeric job flags and page counters. Microsoft notes that a monitor may mark PRINTED when a job is merely sent to the printer. Submission/completion/page counters therefore do not authorize deductions. [JOB_INFO_1](https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-1).
- JOB_INFO_2 can expose pDevMode plus additional job metadata. DEVMODE can describe requested copies and duplex settings, but selected ranges may already be rendered by the originating application, and requested configuration is not confirmation of paper output. Driver/print-processor handling needs validation before treating even planned sheet estimates as reliable. No new undocumented driver calls or spool-file parsing were added. [JOB_INFO_2](https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-2), [DEVMODE](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-devmodew).
- Spooler change notifications can improve capture of short jobs but indicate spooler changes, not physical consumption. Adding them would not justify automatic subtraction. [Printer change notifications](https://learn.microsoft.com/en-us/windows/win32/printdocs/findfirstprinterchangenotification).
- PrintService Operational/event 307 is another possible job-history source, not a physical sheet sensor. This PC's Operational log was inspected read-only and is currently disabled; it was not enabled or used to infer counts. Events/logs were not changed.

## Persistence and failure behavior

Data is per Windows user at:

```text
%LOCALAPPDATA%\SnapPrintMonitoringDesktop\paper-inventory.json
```

The versioned JSON stores only inventory metadata, count/threshold, last operator action and synchronization information. It contains no credentials or document/owner names. A dedicated file lease prevents competing instances from overwriting stock. Saves use a flushed temporary file and same-directory replacement. The UI performs storage work in background tasks. On restart, saved numbers/thresholds/timestamps remain, but downtime requires reconciliation.

Corrupt, unsupported or inaccessible files are not silently reset or overwritten. The dashboard and authenticated API show an inventory storage error and untrusted/unknown or stale counts. A failed operator save is explicitly rejected and does not modify the in-memory estimate. If recovery later succeeds, a refill alone cannot erase storage-related uncertainty. With the app closed, preserve a copy of any damaged file and restore a known valid backup before recounting. Do not hand-edit the file while the app is running. Using a different Windows account uses a separate inventory.

## Read-only API

GET /api/printers keeps all printer discovery data. It adds:

- `paperInventory` on each supported installed printer (null for other queues).
- Root `paperInventories` with both tracked printers, even when Windows queues are missing.
- Root `inventoryError` for local storage problems.

Example entry:

```json
{
  "printerName": "Canon MG3000 series",
  "estimatedSheetsRemaining": 2,
  "lowPaperThreshold": 3,
  "lastInventoryUpdate": "2026-10-14T11:00:00Z",
  "synchronizationStatus": "RECONCILIATION_REQUIRED",
  "reconciliationReason": "Windows print activity observed; physical sheets used are unconfirmed.",
  "confidence": "OPERATOR_ESTIMATE_UNVERIFIED",
  "lowPaperWarning": true,
  "emptyWarning": false,
  "reconciliationRequired": true,
  "physicalCountVerified": false,
  "automaticDeductionEnabled": false,
  "persistenceStatus": "AVAILABLE"
}
```

Additional revision/last-adjustment metadata supports local save safety. The API exposes no refill, adjustment or printing command. Existing bearer authentication, HTTPS, allowed-client restrictions and Firebase-disabled default remain unchanged. Android is not modified.

## Files changed

- PaperInventory.cs: immutable inventory/API model, local persistence, observation/reconciliation policy and validated/idempotent operator changes.
- InventoryDialog.cs: counted total, refill, confirmed usage and threshold controls.
- PrinterSnapshot.cs: additive inventory fields.
- Form1.cs: background persistence/observation integration, safe update handling and shutdown cleanup.
- PrinterDashboard.cs: inline inventory, warnings, controls and diagnostic details; existing two-printer presentation and physical status preserved.
- Tests/InventoryChecks.cs: persistence, restart/gaps, repeated jobs, independent printers, bounds, stale edits, duplicate requests, corrupted storage and failed/recovered saves.
- Tests/DashboardChecks.cs, Program.cs, LanHttpsChecks.cs, csproj: UI/control checks and authenticated inventory response tests; no added package.

## Validation / manual checks

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project .\SnapPrintMonitoringDesktop.Tests --no-build
dotnet run --project .\SnapPrintMonitoringDesktop.Tests --no-build -- --ui-checks
pwsh -NoProfile -File .\scripts\Test-LanHttps.ps1
```

Build: zero warnings/errors. **36 inventory checks, 46 status/API checks, 19 UI checks and 10 HTTPS regression checks passed.** Inventory tests used synthetic jobs and temporary files, not actual tray counts. Atomic file replacement testing required normal Windows permissions beyond the restricted tool sandbox. Dashboard/dialog previews were rendered offscreen at the minimum/default/wide sizes. No physical paper-consumption or Android tests were performed in this task.

On the deployment PC, with the existing secure server settings:

1. Count each idle printer's loaded sheets and save separate totals/thresholds. Confirm independent values.
2. Set a test count of 3, then 0, to inspect LOW/EMPTY warnings (these are test values, not actual hardware evidence). Restore actual counted totals afterward.
3. Restart the app: counts/thresholds/timestamps should survive and require reconciliation for downtime.
4. Start read-only diagnostics before a print you initiate manually. Verify stock is not automatically deducted, and observed activity requests reconciliation. Record actual sheets used or recount.
5. Try a counted-total save while jobs are active; it must not claim synchronization. After printing stops, recount and save again.
6. Refill while reconciliation is pending; the numeric estimate increases but uncertainty remains. Check GET /api/printers with existing authenticated test commands and ensure both inventories remain available when a queue is missing.

UI previews use synthetic low/empty values. High-DPI and prolonged manual operation still require deployment-PC checks. No kiosk/Android/Arduino changes, print commands, driver/service changes, firewall/trust-store changes, commits, pushes or deployment were performed.

Windows notifications follow-up: [NOTIFICATIONS.md](NOTIFICATIONS.md) documents saved-change LOW/EMPTY alerts, persisted suppression state, the inventory-safe test option and validation results.
