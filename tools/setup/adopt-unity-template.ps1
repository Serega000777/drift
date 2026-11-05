# Copies Packages/ and ProjectSettings/ (and optionally Assets/Settings) from a fresh
# "Universal 3D" Unity template project into this repository's UnityProject/ folder.
# Usage: ./tools/setup/adopt-unity-template.ps1 -Template "C:\Projects\DriftTemplate"
param([Parameter(Mandatory = $true)][string]$Template)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$target = Join-Path $repo "UnityProject"
foreach ($dir in @("Packages", "ProjectSettings")) {
    $src = Join-Path $Template $dir
    if (-not (Test-Path $src)) { throw "Not a Unity project (missing $dir): $Template" }
    Copy-Item -Path $src -Destination $target -Recurse -Force
    Write-Host "Copied $dir"
}
$settings = Join-Path $Template "Assets/Settings"
if (Test-Path $settings) {
    New-Item -ItemType Directory -Force -Path (Join-Path $target "Assets") | Out-Null
    Copy-Item -Path $settings -Destination (Join-Path $target "Assets") -Recurse -Force
    Write-Host "Copied Assets/Settings (URP assets)"
}
Write-Host "Done. Unity Hub -> Add project from disk -> $target"
