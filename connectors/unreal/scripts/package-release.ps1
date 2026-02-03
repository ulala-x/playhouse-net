Param(
    [string]$RootDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RootDir)) {
    $RootDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$upluginPath = Join-Path $RootDir "PlayHouseConnector.uplugin"
if (-not (Test-Path $upluginPath)) {
    throw "uplugin not found: $upluginPath"
}

$uplugin = Get-Content -Path $upluginPath -Raw | ConvertFrom-Json
$version = $uplugin.VersionName
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VersionName not found in uplugin"
}

$distDir = Join-Path $RootDir "dist"
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$zipName = "PlayHouseConnector-UE-$version.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

$stagingDir = Join-Path $distDir "staging"
if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir
}
New-Item -ItemType Directory -Path $stagingDir | Out-Null

# Copy plugin sources without build artifacts.
$exclude = @("Binaries", "Intermediate", "Saved", "DerivedDataCache", ".vs", "dist")
robocopy $RootDir $stagingDir /MIR /XD $exclude | Out-Null

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath
Remove-Item -Recurse -Force $stagingDir

Write-Host "Created: $zipPath"
