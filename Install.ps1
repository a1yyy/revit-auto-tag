# RevitToolkit — User-Level Installer
# No administrator rights required.
#
# Revit checks two addin locations on startup:
#   [machine-wide]  C:\ProgramData\Autodesk\Revit\Addins\<ver>\   <- requires admin
#   [user-level]    %APPDATA%\Autodesk\Revit\Addins\<ver>\        <- no admin needed
#
# This script installs to the user-level path, so it works on
# locked-down corporate PCs without IT involvement.

param(
    [string[]] $Versions = @('2022', '2023', '2024', '2025')
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# ── Locate the built files ─────────────────────────────────────────────────────
$dll   = Join-Path $scriptDir 'RevitToolkit.dll'
$addin = Join-Path $scriptDir 'RevitToolkit.addin'

if (-not (Test-Path $dll) -or -not (Test-Path $addin)) {
    Write-Host ''
    Write-Host '  ERROR: RevitToolkit.dll or RevitToolkit.addin not found next to this script.' -ForegroundColor Red
    Write-Host '  Build the project first (Visual Studio → Build → Release), then re-run.' -ForegroundColor DarkGray
    Write-Host ''
    Read-Host '  Press Enter to exit'
    exit 1
}

# ── Install ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  RevitToolkit Installer' -ForegroundColor Cyan
Write-Host '  Installing to user profile — no admin rights required.' -ForegroundColor DarkGray
Write-Host ''

$installed = 0
$skipped   = 0

foreach ($ver in $Versions) {
    $targetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$ver"

    if (-not (Test-Path $targetDir)) {
        Write-Host "  [ ] Revit $ver  not detected, skipping." -ForegroundColor DarkGray
        $skipped++
        continue
    }

    try {
        Copy-Item $dll   $targetDir -Force
        Copy-Item $addin $targetDir -Force
        Write-Host "  [OK] Revit $ver  installed successfully." -ForegroundColor Green
        Write-Host "       $targetDir" -ForegroundColor DarkGray
        $installed++
    }
    catch {
        Write-Host "  [!!] Revit $ver  failed: $_" -ForegroundColor Red
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ''
if ($installed -gt 0) {
    Write-Host "  Done. Plugin installed for $installed Revit version(s)." -ForegroundColor Green
    Write-Host '  Restart Revit — look for the Toolkit tab in the ribbon.' -ForegroundColor DarkGray
} elseif ($skipped -eq $Versions.Count) {
    Write-Host '  No Revit installations found on this machine.' -ForegroundColor Yellow
    Write-Host "  Expected one of: $($Versions -join ', ')" -ForegroundColor DarkGray
    Write-Host "  If your version is newer, run:  .\Install.ps1 -Versions 2026" -ForegroundColor DarkGray
}
Write-Host ''

Read-Host '  Press Enter to close'
