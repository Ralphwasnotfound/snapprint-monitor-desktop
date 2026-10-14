# IP-only update to 192.168.8.103 — final review

Verified 2026-10-15, Asia/Shanghai. IP-ONLY UPDATE VERIFIED — READY FOR FINAL REVIEW, for the current DHCP lease. The address is not reserved permanently.

## Changes in this update

| Repository / configuration | Exact change |
| --- | --- |
| print-and-snap / Services/DocumentPrinting/UploadServices.cs | GetLocalIPAdress returns 192.168.8.103 instead of 192.168.8.100. Existing document upload/download QR generation uses this address; TCP 3000 and listener behavior are unchanged. |
| print-and-snap / Services/PhotoPrinting/PhotoUploadServices.cs | Photo-upload QR IP literal changed from .100 to .103. Its existing TCP 3001 port and all photo processing behavior are unchanged. |
| snapprint-monitor-android / app/src/androidTest/java/com/example/snapprintmonitor/RouterMigrationInstrumentedTests.kt | Live migration target/current assertions changed to .103, prior rollback assertion to .101. No production Android code or APK, CA, Keystore, UI or feature changes were needed. Only the test APK was replaced. |
| Windows protected LAN settings.json | DesktopIp .101 → .103. SamsungIp remains .102, HTTPS 5443, localhost 5050. |
| Windows protected server.pfx / server.cer | New .103 IP-SAN leaf, signed by the unchanged CA. Prepared copies server-ip103.pfx/.cer also remain protected in the same directory. |
| Windows firewall | New SnapPrint-IP103-HTTPS-5443: inbound TCP 5443, Private only, local .103, remote .102, latest executable only. Old SnapPrint-OriginalRouter-HTTPS-5443 disabled after real Samsung .103 API verification. Kiosk TCP 3000 rule unchanged. |
| Samsung saved connection | User scanned the new desktop QR and explicitly selected Verify and pair. Existing token retained; prior .101 endpoint retained in encrypted rollback storage. |
| Desktop documentation | This report and historical-deployment pointers in SECURE_LAN_SETUP.md and ORIGINAL_ROUTER_MIGRATION.md. |

No router, IP assignment, routing/DNS, Arduino, printing, payment, pricing, camera, document-processing or UI behavior changes were made. Historical documents and synthetic QR fixtures retain historical addresses. No commits were made during verification; local builds were launched for testing. The subsequently authorized wrap-up commits only the isolated IP migration changes/documentation. Earlier monitoring implementation and deployment documents remain uncommitted. No push, deployment, publishing or release was performed.

## Endpoints and executable paths

- Document kiosk: http://192.168.8.103:3000 (previous QR host .100).
- Existing photo-upload QR: http://192.168.8.103:3001 (previous QR host .100; not separately camera-tested).
- Monitoring: https://192.168.8.103:5443 (previous .101).
- Authenticated localhost: http://127.0.0.1:5050, unchanged.
- Samsung: 192.168.8.102 on B315_234F5 Wi-Fi.
- Kiosk build launched: C:\CODE\print-and-snap\bin\x64\Release\Snap&Print.exe.
- Monitor: C:\CODE\snapprint-monitor-desktop\SnapPrintMonitoringDesktop\bin\Release\net10.0-windows\SnapPrintMonitoringDesktop.exe --persistent-lan.
- Final running monitoring server PID: 8520, listening only on .103:5443 and 127.0.0.1:5050. Temporary pairing dialogs are not additional monitoring servers.

The kiosk portal is started by the existing upload-screen workflow. At final listener inspection, that portal was no longer running; its successful startup, QR and phone access were confirmed by the user's actual test. It was not converted into an always-running background service.

## Security verification

Leaf: RSA 3072, SHA-256, serverAuth EKU, IP SAN 192.168.8.103. Expires 2027-01-13 02:06:56 +08:00.

Leaf SHA-256: B0B30E975E7CAF0422D9FF5BF51CEC1AC6BDD0F59FC32304D2CC55008DBEEA37.

Unchanged CA SHA-256: 406444114CA0536A8E0C3BFEE845CE35FCE1CD02C9548BFE4D34B63EE12F401E.

The protected credentials.dpapi file is byte-for-byte unchanged before/after migration and restart. The CA private key, token and certificate password were never logged or copied to Android. Existing app-scoped CA trust and strict certificate/IP checks remain enabled. Samsung's wrong-token test returned 401; its mismatched TLS peer-name test failed as required. No certificate-store installation or TLS bypass was used.

## Build and test results

- Kiosk Release/x64 build passed with six existing warnings (duplicate using directives and unused fields), no errors. Product source diff is exactly two IP-literal replacements.
- Android offline Gradle unit-test task and test-APK build passed; unit tests were up-to-date. Production app was not replaced or cleared.
- Samsung pre-pair .103 check passed for health, printers, Current Printing and Print History, proving existing pairing was preserved until confirmation.
- Post-pair encrypted .101 rollback/same-token check passed.
- 12 Samsung QR, security, live TLS/API and UI tests passed. Dashboard Canon/Epson queue/USB/physical status, paper estimates and warning labels matched the desktop API; Current Printing and Print History matched desktop snapshots.
- Real desktop restart recovery passed: connected → offline/stale → automatically connected, stale marker removed, saved pairing unchanged.
- Desktop 18 persistent LAN security checks and existing QR/model/inventory/read-only API regression checks passed, without physical printing.
- Internet IPv4 TCP 443 to 1.1.1.1 passed through Ethernet 7/index 32, source 192.168.254.172. IPv6 TCP 443 to 2606:4700:4700::1111 passed through index 32. Microsoft and Google HTTPS returned 200 without captive-portal redirects.
- Offline router ping from .103 passed. Both adapters remain Up. Active/persistent interface routing settings matched the pre-change backup.
- Offline IPv6 DNS remains empty and IPv4 DNS remains the internet router. Internet IPv6 DNS fe80::1 was learned automatically after the backup; this is an allowed dynamic change on the internet interface, not a configuration edit.
- Offline monitoring evidence: Samsung .102 used original-router Wi-Fi, mobile data off, CAPTIVE_PORTAL/no VALIDATED capability. Live .103 API/UI/restart tests passed in this state. Windows internet was preserved; no test disconnected every Windows internet path.

### User-performed real-device checks — PASSED

The user personally confirmed: Windows showed the pairing QR; Samsung camera scanned it; Verify and Pair succeeded at .103:5443; the kiosk document-upload screen opened; Samsung camera scanned its QR; upload page opened at .103:3000. No QR/token was shared.

File uploading, payment, physical printing, and the photo-upload camera workflow were not tested. No claim of physical printer readiness is made. Printer/payment implementation remained unchanged, established by the limited source diff, not a hardware transaction test.

## Rollback assets and exact procedure

Source/network/firewall baseline: C:\CODE\snapprint-monitor-desktop\artifacts\ip-only-103-rollback.

Protected old .101 monitor configuration/certificate/encrypted credentials: C:\Users\Administrator\AppData\Local\SnapPrintMonitoringDesktop\lan\rollback-ip-only-103. The Android old .101 connection remains in its Keystore-encrypted connection-rollback store.

Rollback is a future authorized operation, not performed here. The original .101 endpoint is not currently owned by Windows; restoring those settings alone will not produce a usable server. Do not assign .101 or enable an old firewall rule until that address/network is independently approved and verified.

1. Close the kiosk normally. Restore its two source files and rebuild the same configuration:

```powershell
$backup = 'C:\CODE\snapprint-monitor-desktop\artifacts\ip-only-103-rollback'
Copy-Item -LiteralPath "$backup\UploadServices.cs" -Destination 'C:\CODE\print-and-snap\Services\DocumentPrinting\UploadServices.cs'
Copy-Item -LiteralPath "$backup\PhotoUploadServices.cs" -Destination 'C:\CODE\print-and-snap\Services\PhotoPrinting\PhotoUploadServices.cs'
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\CODE\print-and-snap\PrintAndSnap.csproj' /t:Build /p:Configuration=Release /p:Platform=x64 /verbosity:minimal /nologo
```

Those QR URLs will revert to .100; they require .100 ownership to work. Do not change Windows IP automatically as part of source rollback.

2. If the old .101 endpoint is approved and available, close only the persistent monitoring server normally, then restore its old settings and leaf files. Keep the unchanged live credentials.dpapi and root CA files:

```powershell
$lan = Join-Path $env:LOCALAPPDATA 'SnapPrintMonitoringDesktop\lan'
foreach ($name in 'settings.json','server.pfx','server.cer') {
    Copy-Item -LiteralPath (Join-Path "$lan\rollback-ip-only-103" $name) -Destination (Join-Path $lan $name)
}
Enable-NetFirewallRule -Name 'SnapPrint-OriginalRouter-HTTPS-5443'
Start-Process -FilePath 'C:\CODE\snapprint-monitor-desktop\SnapPrintMonitoringDesktop\bin\Release\net10.0-windows\SnapPrintMonitoringDesktop.exe' -ArgumentList '--persistent-lan' -WindowStyle Normal
```

3. On Samsung, privately scan a fresh QR from the restored server and explicitly Verify and pair. Do not uninstall, clear data, erase the Keystore, or rotate the token. Validate the restored endpoint, then disable SnapPrint-IP103-HTTPS-5443. Never enable the broad 5050 rule.

4. Restore only the migration test source if desired:

```powershell
Copy-Item -LiteralPath "$backup\RouterMigrationInstrumentedTests.kt" -Destination 'C:\CODE\snapprint-monitor-android\app\src\androidTest\java\com\example\snapprintmonitor\RouterMigrationInstrumentedTests.kt'
```

No production Android source rollback is needed. Do not restore old routing/DNS snapshots for an IP-only application rollback; the working dual-network fixes were not changed.

## Remaining operational risks

- .103 is DHCP-assigned inside the router's .100–.200 pool, not permanently reserved. A later lease/address change can break kiosk QR, monitoring listener, SAN and firewall. Permanent reservation remains a separate approval task; it did not block these tests.
- Existing offline IPv4 default route still appears in ActiveStore, but IgnoreDefaultRoutes/metrics preserve internet selection. This update did not modify that pre-existing routing state.
- Renew the .103 leaf before expiry. Preserve the existing CA and DPAPI credentials.
- GUI automation could not capture/control the Windows windows reliably. Real camera/UI checks above are explicitly attributed to the user's actual verification; no unattended camera test is claimed.
