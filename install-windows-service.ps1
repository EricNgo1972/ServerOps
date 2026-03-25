param(
    [string]$PublishDirectory = "C:\ServerOps",
    [string]$ServiceName = "ServerOps",
    [string]$Urls = "http://0.0.0.0:8080"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    throw "install-windows-service.ps1 must be run as Administrator."
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$appDll = Join-Path $PublishDirectory "ServerOps.Web.dll"

if (-not (Test-Path $appDll)) {
    throw "ServerOps.Web.dll was not found at '$appDll'. Publish the app first."
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if ($existingService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$binaryPath = "`"$dotnet`" `"$appDll`" --urls `"$Urls`""

sc.exe create $ServiceName binPath= $binaryPath start= auto | Out-Null
sc.exe description $ServiceName "ServerOps Web" | Out-Null
sc.exe start $ServiceName | Out-Null

Write-Host "Installed Windows service '$ServiceName'."
