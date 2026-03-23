param (
    [string]$AppName,
    [string]$ZipPath
)

$target = "C:\ServerOps\Apps\$AppName"

Write-Host "Stopping service"
Stop-Service $AppName -Force

Write-Host "Backup"
if (Test-Path "$target\current") {
    Copy-Item "$target\current" "$target\backup_$(Get-Date -Format yyyyMMddHHmmss)" -Recurse
}

Write-Host "Deploy"
Remove-Item "$target\current" -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive $ZipPath "$target\current" -Force

Write-Host "Starting"
Start-Service $AppName

Write-Host "Done"
