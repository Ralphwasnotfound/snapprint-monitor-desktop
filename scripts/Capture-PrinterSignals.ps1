#requires -Version 7.0
param([ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Label = 'current', [ValidateRange(1,1200)][int]$Samples = 1, [ValidateRange(1,30)][int]$IntervalSeconds = 1)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'diagnostics'
New-Item -ItemType Directory -Force $directory | Out-Null
$dll = Join-Path $root 'SnapPrintMonitoringDesktop.Tests/bin/Debug/net10.0-windows/SnapPrintMonitoringDesktop.Tests.dll'
$path = Join-Path $directory ($Label + '-signals.jsonl')
if (Test-Path -LiteralPath $path) { throw 'Capture already exists; select a new label.' }
$stopPath = Join-Path $directory ($Label + '-sampling.stop')
if (Test-Path -LiteralPath $stopPath) { throw 'Stop marker already exists; select a fresh label.' }
for ($i = 0; $i -lt $Samples; $i++) {
    if (Test-Path -LiteralPath $stopPath) { break }
    $json = & dotnet $dll --capture-signals
    if ($LASTEXITCODE -ne 0) { throw 'Read-only signal query failed.' }
    $snapshot = ($json -join "`n") | ConvertFrom-Json
    $snapshot | ConvertTo-Json -Depth 16 -Compress | Add-Content -LiteralPath $path
    $snapshot.printers | Where-Object { $_.name -in @('EPSON L3210 Series','Canon MG3000 series') } |
        Select-Object name,physicalStatus,@{n='Connection';e={$_.evidence.connectionStatus}},@{n='Queue';e={$_.evidence.queueStatus}},@{n='Jobs';e={$_.evidence.jobs.Count}} | Format-Table
    if ($i + 1 -lt $Samples) { Start-Sleep -Seconds $IntervalSeconds }
}
Write-Host "Saved read-only diagnostic samples to $path"
