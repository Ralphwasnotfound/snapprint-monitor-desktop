[CmdletBinding()]
param([string]$ConfigurationPath = (Join-Path $PSScriptRoot 'monitor-config.example.psd1'))
$ErrorActionPreference='Stop'
$config=Import-PowerShellDataFile -LiteralPath $ConfigurationPath
$exe=Join-Path $PSScriptRoot 'SnapPrintMonitoringDesktop.exe'
if(!(Test-Path -LiteralPath $exe)) { throw 'Application executable is missing. Extract the complete ZIP first.' }
$names=@('SNAPMON_BIND_IP','SNAPMON_PORT','SNAPMON_ALLOWED_CLIENTS','SNAPMON_CERT_PATH','SNAPMON_TOKEN','SNAPMON_CERT_PASSWORD')
$previous=@{}
foreach($name in $names) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
function Read-PrivateValue([string]$Prompt) {
    $secure=Read-Host $Prompt -AsSecureString
    $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $secure.Dispose() }
}
try {
    $env:SNAPMON_BIND_IP=[string]$config.BindIP
    $env:SNAPMON_PORT=[string]$config.Port
    $env:SNAPMON_ALLOWED_CLIENTS=@($config.AllowedClients)-join ','
    $env:SNAPMON_CERT_PATH=[string]$config.CertificatePath
    $env:SNAPMON_TOKEN=Read-PrivateValue 'API token (at least 32 UTF-8 bytes; hidden input)'
    $env:SNAPMON_CERT_PASSWORD=if($config.CertificatePath) { Read-PrivateValue 'PFX password (hidden input)' } else { '' }
    $process=Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -PassThru
    $process.WaitForExit()
    if($process.ExitCode -ne 0) { throw ('Monitor exited with code '+$process.ExitCode) }
}
finally { foreach($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') } }
