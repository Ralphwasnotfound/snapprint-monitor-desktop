# Windows low-paper notifications

## Mechanism

This unpackaged WinForms app has no Windows App SDK dependency, package identity or toast-registration setup. It uses the supported **System.Windows.Forms.NotifyIcon.ShowBalloonTip** fallback. No package, registry/COM registration, internet dependency or Windows settings change was added. LOW uses the warning icon; EMPTY uses the error icon. Both are non-blocking. Clicking a balloon or double-clicking its tray icon opens/restores the dashboard. Closing the monitor still exits normally; it is not changed into a close-to-tray app.

Native toast delivery can be considered with a future installer/registered application identity. Microsoft's supported modern path uses AppNotificationManager and its registration infrastructure; adding that is unnecessary for this fallback. [WinForms NotifyIcon overview](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-overview-windows-forms), [ShowBalloonTip](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.showballoontip?view=windowsdesktop-10.0), [modern desktop app notification registration](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet).

## Trigger and persistence rules

- A successful saved change into positive stock at/below its configured threshold requests **Print and Snap - Low Paper** once for that printer.
- A successful saved change to zero requests **Print and Snap - Paper Empty** once, independently of LOW. A direct jump from healthy to zero requests EMPTY only.
- Bodies include the printer name/count and explicitly say **estimated**, **sensorless**, and **physical paper level is not verified**. Reconciliation/physical-readiness logic is unchanged.
- Repeated refreshes, lower counts within LOW, repeated empty saves and repeated save requests do not resend alerts.
- Re-arm requires an operator-saved **increase in sheets** through Add loaded sheets or Set counted total, with the new count above the configured threshold. Threshold-only changes or a small refill remaining at/below threshold do not re-arm. Raising a threshold into LOW can alert once if LOW eligibility has not already been used; lowering/raising thresholds cannot spam an already-issued LOW.
- Each printer stores its own eligibility flags in `paper-inventory.json` alongside its count. The same atomic save commits count and alert eligibility. Failed saves produce no notification and do not consume eligibility.
- Startup only loads/seeds alert history; it never replays current LOW/EMPTY conditions. Older inventory files without alert metadata silently seed their current condition. Dashboard low/empty warnings remain visible.
- Uninitialized stock never alerts. A first **saved**, initialized low/empty count can alert once; merely loading existing inventory cannot.
- Separate Canon/Epson requests are queued with 15-second spacing between Windows calls. Unsent queued messages are refreshed to the latest estimate or canceled when a refill/changed warning state makes them obsolete. Repeated test-button clicks are coalesced while a test is pending/showing.

`Alerts.LowIssued` / `Alerts.EmptyIssued` mean eligibility was consumed when the save committed, **not confirmed Windows delivery**. This deliberately favors avoiding restart spam: a crash after save but before display, application exit with a queued request, or OS suppression can lose a notification. There is no startup replay or automatic repeated retry. Use the safe notification test to check presentation. Already submitted Windows balloons cannot be reliably retracted after a subsequent refill.

## Safe manual notification test — no inventory changes

In the new dashboard, click **Test notification**. Expect:

- Title: Print and Snap - Notification Test
- Body starts TEST ONLY and says no printer, inventory or alert eligibility was changed.

Open Diagnostics to see whether a Windows request was made or an exception was caught. “Requested” is not delivery confirmation. If another alert is active, a test may wait in the queue.

A standalone option works without starting the monitor's inventory service, printer discovery, Firebase or HTTP API. It sends one TEST balloon, runs its Windows message loop for 20 seconds, cleans up the tray icon and exits. No token/certificate environment is needed. The existing monitor may remain running.

The validation build from this task is available at:

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
Start-Process -FilePath '.\diagnostics\notifications-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.exe' -ArgumentList '--test-notification' -WindowStyle Hidden -Wait
```

The isolated mode was actually run and exited with code 0. On-screen Windows presentation was **not visually confirmed**. No printing or inventory mutation is performed by that code path.

## Rebuild / run tests

A monitor process was already running and locked the default EXE. It was not stopped. The initial default-output build failed on that lock; a separate validation output built cleanly with zero warnings/errors:

```powershell
Set-Location C:\CODE\snapprint-monitor-desktop
dotnet build .\SnapPrintMonitoringDesktop.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BaseOutputPath=C:\CODE\snapprint-monitor-desktop\diagnostics\notifications-build\
dotnet .\diagnostics\notifications-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
dotnet .\diagnostics\notifications-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll --ui-checks
```

Close the normal monitor yourself before rebuilding/relaunching the default output. Restart with its existing secure server configuration; the already-running instance cannot acquire new source changes. Do not launch another full monitor against the same inventory file. The standalone test option above is safe alongside it.

To run the HTTPS regression without conflicting with port 5050, pick a free port on this PC (the example probes an available port, then releases it for the test):

```powershell
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Parse('192.168.254.120'), 0)
$probe.Start()
$testPort = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
$probe.Stop()
pwsh -NoProfile -File .\scripts\Test-LanHttps.ps1 -Port $testPort -TestAssemblyPath .\diagnostics\notifications-build\Debug\net10.0-windows\SnapPrintMonitoringDesktop.Tests.dll
```

Actual results: **29 notification policy/persistence checks, 36 existing inventory checks, 46 status/API checks, 20 UI checks, and 10 HTTPS checks passed**. HTTPS used temporary port 58892, not the running monitor's port. Policy tests use temporary files/fake inventory and send no Windows notifications. They cover 10→3 LOW, 3→2 suppression, 2→0 EMPTY, 0→10 refill reset, 10→3 again, independent printers, restart and old-file migration, failed saves, duplicate requests and queued-message refresh/cancellation.

## Operator workflow test (only use real inventory changes)

Use the standalone/button test for safe presentation checks. The automated policy suite tests the numeric scenario without touching real stock. If checking the complete operator workflow later, use truthful counts/confirmed usage:

1. With 10 actual sheets entered and threshold 3, record 7 physically confirmed sheets used. Expect one LOW message for that printer and a 3-sheet estimate.
2. Record 1 additional confirmed sheet used: estimate 2, no new LOW notification.
3. Record 2 additional confirmed sheets used: estimate 0, one EMPTY message.
4. Physically add and record 10 sheets using Add loaded sheets: eligibility re-arms. A subsequent confirmed reduction to 3 can generate LOW again.
5. Repeat independently for Epson. If requests occur together, allow the queue delay.
6. Restart at LOW/EMPTY: existing warnings remain, with no startup notification replay.

Do not enter fictitious inventory or print test pages just to test notifications. Nothing automatically deducts paper; the existing reconciliation requirements still apply.

## Files changed

- PaperAlertPolicy.cs: message content, per-printer eligibility policy, old-data seeding and pending-message reconciliation.
- WindowsPaperNotifier.cs: supported tray presentation, non-blocking queue, test mode, exception diagnostics and cleanup.
- PaperInventory.cs: persisted alert state and notification returned only after a successful atomic save.
- Form1.cs: save-result dispatch on the UI thread, pending-message refresh, safe test event and lifetime cleanup.
- Program.cs: isolated --test-notification mode.
- PrinterDashboard.cs: Test notification button and notification diagnostics; paper indicators remain.
- Tests/NotificationChecks.cs, DashboardChecks.cs, Program.cs, csproj: policy/persistence and safe UI-option coverage.
- scripts/Test-LanHttps.ps1: optional TestAssemblyPath for validating a build outside the locked default output.
- NOTIFICATIONS.md and PAPER_INVENTORY.md: behavior, manual tests, outcomes and limitations.

## Windows limitations

Windows settings, Do Not Disturb/Focus Assist, accessibility timing, organizational policy, Explorer availability, noninteractive sessions and notification suppression can prevent or delay a balloon. Windows may render it in a modern notification surface or under a generic application/system identity. Requested display time is advisory. Tray icons may be in the overflow area. No setting/policy was changed to force visibility, sound, lock-screen access or registration. Dashboard warnings remain the dependable in-app indicator while the app is open. Notifications do not run while the application is closed, and lack of a balloon does not mean stock is adequate.

No Android/kiosk/Arduino changes, printing, firewall/trust-store changes, commits, pushes or deployment were performed. Authentication, HTTPS, full printer discovery and Firebase-disabled behavior are preserved.
