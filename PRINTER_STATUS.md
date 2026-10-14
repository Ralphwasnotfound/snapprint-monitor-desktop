# Physical status investigation and manual validation

## Root cause and actual controlled observations

Windows queue Normal/idle is not proof of physical power or readiness. Microsoft documents that WMI can return Idle when drivers do not supply current status to the spooler. PrintQueue and native GetPrinter are additional views of spooler/driver data, not independent physical probes.

A concrete implementation defect was found: WMI DetectedErrorState and ExtendedDetectedErrorState value 2 (No Error) was mapped to ERROR. Value 9 (Service Requested) was mapped to OFFLINE. Both are corrected. Low paper/toner indications are warnings. This is a plausible source of the previously reported successful-print ERROR, but that incident cannot be attributed retrospectively without its raw sample or the originating window.

On 2026-10-14, the user physically performed controlled transitions while the assistant captured read-only signals. Saved files under diagnostics (ignored by Git) record timestamps and stated hardware conditions:

| Printer / observed condition | USB printer node present | WorkOffline | WMI status/extended/errors |
|---|---|---|---|
| Epson ON, USB connected | true | false | 3 / 2 / 0,0 |
| Epson OFF, USB connected | false | true | 3 / 2 / 0,0 |
| Epson OFF, USB disconnected | false | true | 3 / 2 / 0,0 |
| Epson ON, USB reconnected | true | false | 3 / 2 / 0,0 |
| Canon ON, USB connected (initial) | true | false | 3 / 2 / 0,0 |
| Canon OFF, USB connected | false | true | 3 / 2 / 0,0 |
| Canon OFF, USB disconnected | false | true | 3 / 2 / 0,0 |
| Canon ON, USB reconnected | true | false | 3 / 2 / 0,0 |

These observations verify that device presence changed with these particular printers' state changes on this PC. They do not establish a universal power-state detector: OFF and cable-disconnected are indistinguishable, and other printers may retain USB electronics while mechanically powered off. During investigation an earlier Epson query listed it present before later OFF baseline queries showed absent; timing matters. Both devices were restored ON/connected by the user.

After implementation, exact queue metadata maps Canon to USBPRINT\CANONMG3000_SERIES\8&38EA7361&0&USB002 and Epson to USBPRINT\EPSONL3210_SERIES\8&9CEBB97&0&USB001. CM parent traversal found their respective USB printing interfaces (Canon VID_04A9/PID_180B; Epson VID_04B8/PID_1188). These are observations, not hardcoded mappings. No port-name suffix or friendly-name match is used to assign devices. Queue registry metadata may be stale; duplicate queue-to-device associations are marked ambiguous. Missing metadata, inaccessible queries and unknown CM errors preserve UNKNOWN. A non-present exact node does not prove cable removal rather than power-off.

## Implementation and API semantics

- WindowsPrinterMonitor.cs: corrected WMI enums, retained discovery fallback and raw WMI query diagnostics; adds read-only signal enrichment.
- PrinterSignals.cs: native GetPrinter(PRINTER_INFO_6), EnumJobs(JOB_INFO_1), refreshed System.Printing.PrintQueue, exact registry PnPData association, Configuration Manager connected devnode/status/problem/parent queries. Buffers and printer handles are released. Source failures are diagnostic results, not physical faults. Job capture is capped at 256 entries per queue.
- PrinterSnapshot.cs: additive physicalStatus and evidence fields. reportedStatus now describes **physical status** and remains UNKNOWN with these signals. Consumers that previously treated reportedStatus as a queue state must instead read evidence.queueStatus.
- Form1.cs: explains and displays separate physical/USB/queue/job reports.
- Both project files and GlobalUsings.cs: enable the built-in Windows Desktop System.Printing reference through UseWPF, alongside WinForms. No NuGet package added. Explicit IO/HTTP imports preserve existing source behavior.
- Tests/Program.cs: regression scenarios, structured API assertions and --capture-signals diagnostic mode.
- scripts/Capture-PrinterBaseline.ps1: read-only WMI, PnP presence and job baseline snapshots.
- scripts/Capture-PrinterSignals.ps1: repeatable enriched diagnostic samples for controlled transitions/printing.
- .gitignore: excludes local diagnostic evidence.

PhysicalStatus=UNKNOWN and physicalReadinessVerified=false mean no supported vendor protocol establishes actual physical readiness. USB_PRESENT/USB_NOT_PRESENT describe observed OS connectivity for an exact queue device association. The connection observation is inferred about this printer from registry association; it is not verified power/readiness. Queue states (including ERROR/OFFLINE) are explicitly Windows-reported. Conflicting classifications are shown as CONFLICTING_WINDOWS_REPORTS with each source preserved. Jobs expose ID, raw flags and page counts, never document/owner names. Windows job completion/printed flags do not prove paper emerged; error and completion flags may coexist. Source exceptions, timestamps, numeric values, error attribution and final decision rationale are included in API evidence and UI diagnostics.

Installed Canon BJ and Epson language monitors and EnableBIDI=True were observed, but no documented, validated vendor-specific physical telemetry protocol was established. The application does not invoke undocumented driver entry points or send device/print commands.

## Exact manual commands (PowerShell 7)

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project .\SnapPrintMonitoringDesktop.Tests --no-build
pwsh -NoProfile -File .\scripts\Test-LanHttps.ps1
# All sources for the current hardware state:
pwsh -NoProfile -File .\scripts\Capture-PrinterSignals.ps1 -Label current-state
```

Choose a fresh label each time; captures never overwrite prior evidence. A read-only inventory command may need the same local privileges as Get-PnpDevice/Get-CimInstance. Do not run remote share discovery or change service/driver settings to get a measurement.

For additional native-collector hardware validation, on ONE printer at a time while the other stays unchanged:

1. Visually confirm ON and USB connected; wait 15 seconds; capture with label printer-on.
2. Turn OFF with cable connected; wait 15 seconds; capture with label printer-off-connected.
3. Keep OFF and unplug USB; wait 15 seconds; capture with label printer-off-disconnected.
4. Reconnect USB and turn ON; wait 15 seconds; capture with label printer-restored.

Use unique labels including canon/epson. Expected physical status remains UNKNOWN in every sample; the exact device connection observation should change present/absent/present on these tested devices. Query failure must remain unknown, not absent. Do not remove installed queues to simulate disconnection.

To diagnose the successful-print ERROR (user initiates printing; script never prints):

```powershell
pwsh -NoProfile -File .\scripts\Capture-PrinterSignals.ps1 -Label canon-print-followup -Samples 60 -IntervalSeconds 1
```

Once sampling starts, manually print one small non-private document. Note actual paper output, time, exact error text and which window showed it: Windows queue, vendor utility, monitor, or another app. After capture, inspect samples:

```powershell
$samples = Get-Content .\diagnostics\canon-print-followup-signals.jsonl | ForEach-Object { $_ | ConvertFrom-Json }
$samples | ForEach-Object {
    $time = $_.lastUpdated
    $_.printers | Where-Object name -eq 'Canon MG3000 series' |
        Select-Object @{n='Time';e={$time}},name,evidence
} | ConvertTo-Json -Depth 12
```

Sampling waits one second between queries, not a guaranteed one-second period; native queries can take time and very short jobs/errors can be missed. A disappeared job is not proof of success. If no error is captured, record that outcome rather than inventing an error source. Existing Windows PrintService logs/vendor utility observations can supplement this in a later investigation; this work does not enable logs, change settings, restart the spooler or alter drivers.

## Validation and limits

Controlled Epson and Canon PnP/WMI state transitions were actually observed with the user operating the hardware. Actual physical readiness and universal ONLINE/OFFLINE detection remain unverified. Unit scenarios include powered ON/OFF/disconnected fixtures, idle, successful-job flags, transient spooler errors and ambiguous mappings. Live native/PrintQueue job queries succeeded with both printers ON; queue flags were zero, CM problem codes zero and jobs empty. Authentication, HTTPS, Firebase-disabled behavior and read-only endpoints remain in place. No Android/kiosk edits, firewall/trust-store changes, commits, pushes or deployment.

## Canon manual print capture findings

The user confirmed a successful physical Canon print with no visible error in this reproduction. This confirms availability for that completed physical print, based on the user's observation; it does not justify persistent ONLINE in the application.

The read-only capture contains 60 samples from **18:52:49.791 to 18:54:25.961 Asia/Shanghai on 2026-10-14**. Every sample showed:

| Source | Observed value |
|---|---|
| WMI WorkOffline / PrinterStatus / ExtendedPrinterStatus | False / 3 (Idle) / 2 (Unknown) |
| WMI detected / extended detected error | 0 / 0 (Unknown, not an error indication) |
| Native GetPrinter status flags | 0x00000000 |
| Refreshed PrintQueue flags / NumberOfJobs | None (0x00000000) / 0 |
| EnumJobs | Query succeeded; zero jobs observed |
| Exact USBPRINT device / CM problem code | Present / 0 |
| Physical classification | UNKNOWN |

No sampled transient error, queue status change, job status change or misleading Canon error flag was captured. No job ID or start/end marker was observed, so the data cannot independently identify before/during/after print phases. The physical print's exact timing relative to this sampling window was not established when the summary was written. A short-lived job can be missed by polling; printing outside the sampling window is also possible. No error observed does not prove no transient error occurred.

Existing PrintService Operational logging was disabled. The Admin log was enabled and contained zero records. Neither setting was changed. There are no event records to establish job timing or attribute the earlier ERROR.

The earlier ERROR remains unexplained by this capture. The corrected No Error=2 mapping bug is a concrete possible cause in the old application, but these 60 samples contained error values 0/0, not 2/2, so the bug was not triggered in this run. A Windows/vendor error, a temporary spooler condition, or an observation outside the window remains possible; none is proven.

Recommended next step: retain these samples and source corrections, and reproduce only if the error recurs. Start capture first, record the exact click/physical completion/error times and originating window, then compare raw queue/job flags. If fast jobs keep escaping polling, add a separately scoped, read-only spooler change-notification capture (with a periodic safety refresh) rather than inventing physical status from idle flags. Do not enable logs or alter printer settings implicitly. Vendor-documented bidirectional telemetry would still need independent hardware validation before physical READY/ONLINE claims.

## Repeat capture: job observed and no-error mapping confirmed

The first capture's timing was later confirmed uncertain: the user had waited several minutes before printing. Therefore that first window cannot establish before/during/after job behavior. A longer, explicitly started second capture supersedes it for print-job analysis.

The repeat contains **68 samples, 18:58:21.504–19:00:06.898 China time**. The user confirmed **“Printed successfully, no error.”** Canon job **6** was observed in 8 samples from **18:58:55.037 through 18:59:05.887**.

| Phase | WMI PrinterStatus / DetectedErrorState | Native/PrintQueue flags | Jobs |
|---|---|---|---|
| Before | 3 (Idle) / 0 (Unknown) | Both zero | 0 |
| Job observed | 4 (Printing) / 2 (No Error) | Both stayed zero | Job 6; raw flags 8208 = 0x2010, includes PRINTING; no sampled job-error bits |
| After job disappeared | 3 (Idle) / 0 (Unknown) | Both zero | 0 |

USB presence remained true and device problem code remained zero. WMI WorkOffline stayed false, ExtendedPrinterStatus stayed 2 (Unknown), ExtendedDetectedErrorState stayed 0. Native zero queue flags can coexist with an active job; they do not prove inactivity or physical ONLINE. The collector now treats normal/zero queue flags together with WMI PRINTING as compatible queue reports rather than an error/conflict. Captured job page counters reported PagesPrinted=1 and TotalPages=0; page counters are driver reports, not independent proof of output. No completion flag was sampled before job removal. Physical success is established by the user's observation, not inferred from disappearing jobs.

**Concrete finding:** while a real Canon job was printing successfully, WMI emitted DetectedErrorState=2. The original Map method would deterministically classify that value as ERROR, before reaching its PRINTING branch. The new mapping returns PRINTING for this exact captured tuple (WorkOffline=False, status=4, extended=2, error=2, extendedError=0). This demonstrates a reproducible application false-ERROR defect. It explains how an application ERROR could occur during a successful print. It does **not** identify the originating window or prove that this caused the historical ERROR; that original incident remains untraced.

No transient queue-error flags or job-error bits were observed in the repeat. No misleading Canon error flag was demonstrated; the captured no-error value was valid and the old application interpretation was wrong. Vendor utility internals were not queried. Driver-supplied JOB_INFO_1.pStatus text is not exported for privacy; future samples indicate its presence, and numeric flags are supplemental when it exists. The repeat was captured before that presence field was added, so it cannot establish whether a driver status string existed. No broad claim that every driver-specific signal was error-free is made.

These two real devices' ON/OFF/disconnection observations confirm an OS USB connectivity signal, not a unique power-state/physical-readiness signal. OFF with USB connected and OFF with USB disconnected remain indistinguishable. Successful physical output proves Canon was available for that particular job/time, but cannot justify persistent ONLINE after the job. Keep physicalStatus=UNKNOWN and display the independently labeled connection/queue/job observations.

Exact reproduction of a longer capture (new label required):

```powershell
pwsh -NoProfile -File .\scripts\Capture-PrinterSignals.ps1 -Label canon-next-print -Samples 600 -IntervalSeconds 1
# In a second PowerShell window, AFTER manually printing and allowing return to idle:
New-Item -ItemType File C:\CODE\snapprint-monitor-desktop\diagnostics\canon-next-print-sampling.stop
```

The stop marker ends only that diagnostic session; it does not affect the monitoring app or spooler. Retain the raw JSONL and analysis summaries locally. No test pages were automatically printed. Recommended next step: use the corrected monitor while retaining these diagnostics; if ERROR recurs, record its exact window/text/time and correlate source flags. Consider read-only spooler notifications only if polling fails to capture a recurrence. A vendor physical-readiness implementation remains separate work requiring documented telemetry and controlled hardware validation.

Final validation: solution build succeeded with zero warnings/errors; **43 status/API checks and 9 authenticated LAN HTTPS checks passed** after the final changes. git diff --check reported no whitespace errors. Local discovery still returned seven queues. Hardware transitions and the manual physical print were performed by the user while the assistant observed read-only signals; automated fixtures do not themselves constitute physical hardware testing.

Reference definitions: [Win32_Printer status/error enums and driver reporting caveat](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-printer), [native PRINTER_INFO_6 flags](https://learn.microsoft.com/en-us/windows/win32/printdocs/printer-info-6), [JOB_INFO_1 flags, pStatus and completion caveat](https://learn.microsoft.com/en-us/windows/win32/printdocs/job-info-1), [PrintQueue.IsOffline](https://learn.microsoft.com/en-us/dotnet/api/system.printing.printqueue.isoffline?view=windowsdesktop-10.0).
