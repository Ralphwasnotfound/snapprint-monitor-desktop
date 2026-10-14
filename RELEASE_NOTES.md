# Print and Snap Monitor v1.0.0

First Windows x64 desktop monitoring release for Canon MG3000 series and EPSON L3210 Series.

## Included

USB connection evidence, Windows queue/error status, Current Printing, persistent local Print History, requested printer job settings, simple per-printer paper settings/summaries, LOW/EMPTY tray notifications, default-off automatic-estimate preference, authenticated read-only HTTP localhost / HTTPS private-LAN API, and Firebase-disabled monitoring. Document names and contents are not collected. Android integration is planned next; no Android/kiosk/Arduino changes are included.

## Important limitations

**Automatic paper deduction from real Windows print jobs is unavailable.** The optional preference does not activate working automatic counting. Reported pages, copies/duplex requests and queue completion/disappearance cannot establish physical sheets. Stock remains unchanged for uncertain jobs and needs operator checking. Positive automatic-accounting tests use synthetic validated adapters only.

USB presence does not establish physical ONLINE/readiness. Driver status can be stale or misleading. Three-second polling can miss short jobs and transient errors; Print History is an observed journal, not a complete audit. Reported job totals are not document page counts; N-up handling mode is not pages per sheet. Windows may suppress tray notifications. This release is unsigned; no physical printer, live balloon, Android connectivity or high-DPI hardware verification was performed for the release.

## Validation

- Clean Release build: zero warnings/errors.
- 225 automated synthetic settings/history/accounting/inventory/notification/status/API checks passed.
- 35 offscreen dashboard/dialog/job-view checks passed.
- 10 LAN HTTPS checks passed: encrypted certificate load, authentication rejection/success, IP/trust validation and listener shutdown. Temporary certificate files only; no trust-store/firewall changes.
- Windows x64 self-contained publish and packaged executable smoke check are documented in RELEASE_PREPARATION.md after packaging completes.

Extract the entire ZIP and read README.md before use. Configure a strong API token; LAN mode additionally requires an explicit private IP, valid HTTPS certificate and exact allowed-client addresses. No credentials, keys, user histories or inventory data are shipped. No broad firewall rules, trust installation, router forwarding, autostart or deployment is performed.
