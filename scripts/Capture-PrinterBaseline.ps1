param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Label, [string]$ObservedState)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$queues = @(Get-CimInstance Win32_Printer | Where-Object { $_.Name -in @('EPSON L3210 Series','Canon MG3000 series') } | Select-Object Name,PortName,PNPDeviceID,PrinterStatus,ExtendedPrinterStatus,DetectedErrorState,ExtendedDetectedErrorState,WorkOffline,EnableBIDI,DriverName)
$devices = @(Get-PnpDevice | Where-Object { $_.InstanceId -like 'USBPRINT\*' } | ForEach-Object {
    $present = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_IsPresent -ErrorAction Stop
    [pscustomobject]@{Name=$_.FriendlyName; InstanceId=$_.InstanceId; Status=$_.Status; IsPresent=$present.Data}
})
$jobs = @(foreach ($queue in $queues) { Get-PrintJob -PrinterName $queue.Name -ErrorAction Stop | Select-Object PrinterName,Id,JobStatus,SubmittedTime,TotalPages,PagesPrinted })
$directory = Join-Path $root 'diagnostics'
New-Item -ItemType Directory -Force $directory | Out-Null
$result = [pscustomobject]@{CapturedAt=[DateTimeOffset]::Now; UserObservedState=$ObservedState; Wmi=$queues; UsbDevices=$devices; Jobs=$jobs}
$result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $directory ($Label + '.json'))
$result | ConvertTo-Json -Depth 8
