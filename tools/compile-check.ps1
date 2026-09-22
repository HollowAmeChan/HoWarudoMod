# Offline compile check for Warudo mod scripts.
#
# Compiles every .cs inside Mods/<ModName>/ against the real Warudo DLLs without
# starting Unity, so syntax and API mistakes (namespace shadowing, fields that
# collide with Node base members, ...) show up in seconds instead of minutes.
#
# NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 reads
# BOM-less files as ANSI, so non-ASCII comments here would break parsing.
#
# Usage:
#   powershell -File tools\compile-check.ps1
#   powershell -File tools\compile-check.ps1 -ManagedDir "D:\...\Warudo_Data\Managed"

param(
    [string]$ManagedDir = "D:\steam\steamapps\common\Warudo\Warudo_Data\Managed",
    [string]$CscPath    = "C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\csc.dll"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
# IMPORTANT: never write the produced DLL anywhere Unity imports. Unity ignores any
# folder whose name starts with ".", so ".compile-check" keeps the artifact invisible
# to the asset pipeline (otherwise the DLL would be imported as a plugin).
$outDir   = Join-Path $repoRoot ".compile-check"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path $ManagedDir)) { throw "Warudo Managed folder not found: $ManagedDir" }
if (-not (Test-Path $CscPath))    { throw "Roslyn csc.dll not found: $CscPath (override with -CscPath)" }

# Only the assemblies a mod script actually needs.
$referenceNames = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'Warudo.Core.dll'
)
$references = @()
foreach ($name in $referenceNames) {
    $references += "/reference:" + (Join-Path $ManagedDir $name)
}

$failed = 0
$mods = Get-ChildItem (Join-Path $repoRoot 'Mods') -Directory

foreach ($mod in $mods) {
    $files = @(Get-ChildItem $mod.FullName -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
    if ($files.Count -eq 0) { continue }

    $out = Join-Path $outDir ($mod.Name + ".dll")
    Write-Host ("=== " + $mod.Name + "  (" + $files.Count + " .cs file(s))")

    $arguments = $references + @(
        '/nologo', '/target:library', '/nostdlib+', '/noconfig', '/langversion:9.0',
        ("/out:" + $out)
    ) + $files

    & dotnet $CscPath @arguments
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    OK"
    } else {
        Write-Host ("    FAILED (exit " + $LASTEXITCODE + ")")
        $failed++
    }
}

if ($failed -gt 0) {
    Write-Host ""
    Write-Host ($failed.ToString() + " mod(s) failed to compile")
    exit 1
}

Write-Host ""
Write-Host "all mods compiled"
