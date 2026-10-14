#requires -Version 7.0
[CmdletBinding()]
param([string]$Address = '192.168.254.120', [int]$Port = 5050, [switch]$LaunchDesktop, [string]$TestAssemblyPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$secretDirectory = Join-Path $projectRoot ('.local-https/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
# Remove inherited access BEFORE writing private material. Current user and SYSTEM only.
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.SetOwner($userSid)
foreach ($sid in @($userSid, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
try { Set-Acl -LiteralPath $secretDirectory -AclObject $acl }
catch { Remove-Item -LiteralPath $secretDirectory -Force; throw }
$names = @('SNAPMON_BIND_IP','SNAPMON_PORT','SNAPMON_ALLOWED_CLIENTS','SNAPMON_TOKEN','SNAPMON_CERT_PATH','SNAPMON_CERT_PASSWORD')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:SNAPMON_BIND_IP = $Address
    $env:SNAPMON_PORT = [string]$Port
    $env:SNAPMON_ALLOWED_CLIENTS = $Address
    $env:SNAPMON_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:SNAPMON_CERT_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:SNAPMON_CERT_PATH = Join-Path $secretDirectory 'monitor.pfx'
    $testDll = if ($TestAssemblyPath) { (Resolve-Path -LiteralPath $TestAssemblyPath).Path } else { Join-Path $projectRoot 'SnapPrintMonitoringDesktop.Tests/bin/Debug/net10.0-windows/SnapPrintMonitoringDesktop.Tests.dll' }
    & dotnet $testDll --create-dev-cert
    if ($LASTEXITCODE -ne 0) { throw 'Development certificate creation failed.' }
    & dotnet $testDll --lan-https
    if ($LASTEXITCODE -ne 0) { throw 'LAN HTTPS validation failed.' }
    if ($LaunchDesktop) {
        $exe = Join-Path $projectRoot 'SnapPrintMonitoringDesktop/bin/Debug/net10.0-windows/SnapPrintMonitoringDesktop.exe'
        $desktop = Start-Process -FilePath $exe -PassThru
        Write-Host 'Desktop launched with temporary HTTPS settings. Close its window to finish and remove the development certificate.'
        $desktop.WaitForExit()
    }
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    # Exact generated files only; no recursive directory deletion.
    foreach ($file in @('monitor.pfx','monitor.cer')) {
        $target = Join-Path $secretDirectory $file
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    Remove-Item -LiteralPath $secretDirectory -Force
}
