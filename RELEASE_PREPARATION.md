# v1.0.0 release preparation report

Status: **validated and packaged for v1.0.0 publication**. Publication resumed with authenticated GitHub account Ralphwasnotfound and ADMIN repository permission. The annotated tag and GitHub Release identify the resulting release commit. No production deployment is performed.

## Repository and identity

- Repository: C:\CODE\snapprint-monitor-desktop
- Branch: main
- Origin: https://github.com/Ralphwasnotfound/snapprint-monitor-desktop.git
- Current/base commit: ed624dc888e6ee6527bc4155ff82dff8fa49e761 (unchanged)
- Intended version/tag: v1.0.0
- Intended release title: Print and Snap Monitor v1.0.0
- Remote refs and GitHub release metadata reviewed: no existing tags/releases; version was available at review time. Recheck immediately before publishing.
- User supplied the release author name/email in chat; use that identity for the release commit rather than changing global Git settings.

## Package

- Asset: artifacts/final-v1.0.0/Print-and-Snap-Monitor-v1.0.0-win-x64.zip
- SHA-256 sidecar: artifacts/final-v1.0.0/Print-and-Snap-Monitor-v1.0.0-win-x64.zip.sha256
- Size: 88,583,366 bytes
- SHA-256: 4994518bb31a29e42a8c053995992008fd8939bc54a0acefa961206c92e47d28
- Windows win-x64, self-contained .NET 10, untrimmed, multiple files, application version 1.0.0. No separate .NET runtime install required.
- 626 files including application/runtime dependencies, selected documentation and nonsecret launcher/config template.
- Staged publish output and independently extracted ZIP executable both launched via --release-smoke-check and exited 0. This renders the dashboard and loads WinForms/WPF/server dependencies without binding an API, initializing inventory/history, or printing. It does not validate a normal end-to-end hardware monitoring session.
- Required hostfxr/hostpolicy/coreclr, WinForms, WPF, System.Printing and ASP.NET files verified in package.
- No detected credentials, private keys/certificates, inventory/history JSON, test assemblies, PDBs, logs, diagnostics or portable CLI tooling in ZIP.
- Unsigned executable/ZIP. No installer, autostart, OS firewall or trust modification.

## Validation

Clean Release solution build passed with zero warnings/errors. All 225 automated synthetic/status/API checks, 35 offscreen UI checks and 10 LAN HTTPS checks passed. Live read-only discovery found seven queues. HTTPS validated using a temporary protected IP-SAN certificate on an unused LAN port; no trust-store/firewall changes. Release launch/package scripts parsed successfully. No physical printing, physical sheet accounting, Android connectivity, live balloon delivery or high-DPI hardware validation was performed.

## Review and security

Reviewed current tracked/untracked source/document candidates (56 before this report) against exclusions and common credential/private-key patterns: no matches or forbidden artifact paths. This is a focused review, not a guarantee that arbitrary secrets can be identified by regex. No credentials/config secrets were added. The retained Firebase rollback publisher contains its existing service URL but is not called by the monitor; no Firebase token is present. Job/document names are not collected, and actual local histories/inventory remain outside release contents.

Expanded .gitignore covers artifacts, local certificates and inventory/history data. Existing ignored diagnostics and user files were preserved. Git diff whitespace issues introduced during release prep were corrected. No files from other projects were modified. GitHub CLI was absent; official portable CLI was downloaded only into ignored artifacts/tooling, without system install. Authentication was initially unavailable and has now been completed and verified. Git credential storage was not read/extracted or printed.

## Release preparation changes

- README.md: overview, install/run, per-printer settings, current/history, notifications, strong authentication, private LAN HTTPS, local data and known limitations.
- RELEASE_NOTES.md: features, validation, limitations and planned Android integration.
- .gitignore: exclude local artifacts, certificates and data.
- SnapPrintMonitoringDesktop/SnapPrintMonitoringDesktop.csproj: v1.0.0 product/assembly/file version and stable informational version.
- SnapPrintMonitoringDesktop/Program.cs: isolated --release-smoke-check mode.
- release/Start-Monitor.ps1: hidden token/PFX password prompts, process-local environment with restoration; no credential file/logging.
- release/monitor-config.example.psd1: nonsecret localhost-first template.
- scripts/Publish-Release.ps1: reproducible self-contained publish, selected docs, forbidden-content audit, smoke test, ZIP and checksum. Refuses to overwrite existing artifacts; run from a fresh checkout/output location for a new build.
- RELEASE_PREPARATION.md: this report.

The final release commit must also include the existing completed monitoring source/tests/docs from earlier tasks, which are still uncommitted in this repository. Intended source/tests/docs/templates are explicitly reviewed before staging; ignored local artifacts and data are excluded.

## Publication procedure

Run in a local PowerShell terminal:

```powershell
& 'C:\CODE\snapprint-monitor-desktop\artifacts\tooling\gh\bin\gh.exe' auth login --hostname github.com --git-protocol https --web
```

GitHub browser authorization was completed by the user. No tokens are recorded in this report.

After login, recheck remote refs/releases, review/stage only source/tests/docs/templates, create the authorized release commit with the supplied author identity, push main normally, create annotated v1.0.0 against that commit, push the tag and create the GitHub Release attaching the ZIP and checksum. Do not force-push, overwrite refs/assets or change visibility. The release URL is https://github.com/Ralphwasnotfound/snapprint-monitor-desktop/releases/tag/v1.0.0 ; its target commit is recorded by the annotated tag and the final publication report.

## Limitations to keep in the published release

Automatic paper deduction from real Windows jobs is unavailable. The optional preference does not activate automatic counting. Spooler counters and requested driver settings cannot establish physical sheets; unknown jobs preserve stock and require checking. USB presence is not verified physical readiness. Three-second polling may miss jobs. History is a bounded local observed journal, not a physical print audit. No names/contents are collected. Windows may suppress tray notifications. Android integration is next, not included.

Final-source validation: exactly 17 EOF blank-line tails were trimmed byte-for-byte without changing encoding, existing line endings or source behavior. The staged check, Release build, 225 automated checks, 35 UI checks and 10 HTTPS checks passed again. A fresh final-source self-contained ZIP was produced under artifacts/final-v1.0.0, its SHA-256 verified, and its extracted executable smoke check exited 0. The previous package was preserved.
