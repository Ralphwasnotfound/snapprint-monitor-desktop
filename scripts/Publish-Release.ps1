[CmdletBinding()]
param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric semantic version.' }
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root "artifacts/publish/Print-and-Snap-Monitor-v$Version-win-x64"
$zip=Join-Path $root "artifacts/Print-and-Snap-Monitor-v$Version-win-x64.zip"
if(Test-Path -LiteralPath $folder) { throw 'Publish folder already exists; preserve it and use a fresh workspace for reproducible packaging.' }
if(Test-Path -LiteralPath $zip) { throw 'ZIP already exists; existing artifacts are not overwritten.' }
& dotnet publish (Join-Path $root 'SnapPrintMonitoringDesktop/SnapPrintMonitoringDesktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:UseSharedCompilation=false -p:IncludeSourceRevisionInInformationalVersion=false "-p:Version=$Version" -o $folder
if($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
# Explicitly selected source documentation and nonsecret launcher/templates only.
foreach($doc in @('README.md','RELEASE_NOTES.md','LAN_HTTPS.md','JOB_SETTINGS.md','PRINT_HISTORY.md','OPTIONAL_ESTIMATE.md','AUTOMATIC_PAPER_USAGE.md','PAPER_INVENTORY.md','NOTIFICATIONS.md','SIMPLE_PAPER_UI.md','PHASE1.md','PRINTER_STATUS.md','UI_REDESIGN.md')) { Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $folder }
foreach($file in @('Start-Monitor.ps1','monitor-config.example.psd1')) { Copy-Item -LiteralPath (Join-Path $root "release/$file") -Destination $folder }
$forbidden=Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object { $_.Extension -in @('.pfx','.p12','.pem','.key','.cer','.crt','.der','.pdb','.log') -or $_.Name -match '(?i)(paper-inventory|print-history\.json|\.tests\.|^\.env)' }
if($forbidden) { throw 'Unexpected sensitive/debug/test files found in package.' }
$process=Start-Process -FilePath (Join-Path $folder 'SnapPrintMonitoringDesktop.exe') -ArgumentList '--release-smoke-check' -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(30000)) { throw 'Packaged smoke check timed out. Inspect the identified smoke-test process before continuing.' }
if($process.ExitCode -ne 0) { throw 'Packaged executable smoke check failed.' }
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip -CompressionLevel Optimal
$hash=Get-FileHash -LiteralPath $zip -Algorithm SHA256
($hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output "Created $zip"
