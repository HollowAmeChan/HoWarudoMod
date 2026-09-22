# Offline compile check for the EDITOR side of this repo.
#
# Compiles Editor/*.cs together with every Mods/**/*.cs against the real UnityEditor +
# UnityEngine modules, the UMod SDK DLLs and the shipping Warudo assemblies, without
# opening Unity. Catches signature mistakes in HoModTestBuilder in seconds.
#
# NOTE: ASCII-only on purpose. Windows PowerShell 5.1 reads BOM-less files as ANSI,
# so non-ASCII comments here would break parsing.
#
# Usage:
#   powershell -File tools\compile-check-editor.ps1

param(
    [string]$UnityEditorManaged = "D:\Unity\Unity 2021.3.45f2\Editor\Data\Managed",
    [string]$FrameworkDir       = "D:\Unity\Unity 2021.3.45f2\Editor\Data\MonoBleedingEdge\lib\mono\4.7.1-api",
    [string]$UModPluginDir      = "D:\Unity_Project\BreakWarudo\Warudo-Mod-Tool-0.14.4.8\Packages\UMod\Plugin",
    [string]$WarudoManaged      = "D:\steam\steamapps\common\Warudo\Warudo_Data\Managed",
    [string]$CscPath            = "C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\csc.dll"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
# Must not land where Unity imports files: dot-folders are ignored anywhere in the tree.
$outDir = Join-Path $repoRoot ".compile-check"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

foreach ($p in @($UnityEditorManaged, $FrameworkDir, $UModPluginDir, $WarudoManaged)) {
    if (-not (Test-Path $p)) { throw "Not found: $p" }
}

$sources = @()
$sources += @(Get-ChildItem (Join-Path $repoRoot 'Editor') -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem (Join-Path $repoRoot 'Mods') -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No sources found under Editor/ or Mods/" }

$references = @()
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll',
                    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')) {
    $path = Join-Path $FrameworkDir $name
    if (-not (Test-Path $path)) { throw "Missing framework reference: $path" }
    $references += "/reference:" + $path
}

$references += "/reference:" + (Join-Path $UnityEditorManaged 'UnityEditor.dll')
$references += "/reference:" + (Join-Path $UnityEditorManaged 'UnityEngine.dll')
foreach ($module in Get-ChildItem (Join-Path $UnityEditorManaged 'UnityEngine') -Filter '*.dll') {
    $references += "/reference:" + $module.FullName
}
foreach ($dll in Get-ChildItem $UModPluginDir -Recurse -Filter '*.dll') {
    $references += "/reference:" + $dll.FullName
}
foreach ($name in @('Warudo.Core.dll', 'UnityEngine.CoreModule.dll')) {
    $path = Join-Path $WarudoManaged $name
    if (Test-Path $path) { $references += "/reference:" + $path }
}

$out = Join-Path $outDir "HoWarudoModTests.Editor.dll"
Write-Host ("Compiling " + $sources.Count + " source file(s) ...")

$arguments = $references + @(
    '/nologo', '/target:library', '/nostdlib+', '/noconfig', '/langversion:9.0',
    '/define:UNITY_EDITOR', '/define:UNITY_2021_3_OR_NEWER',
    ("/out:" + $out)
) + $sources

& dotnet $CscPath @arguments
if ($LASTEXITCODE -ne 0) {
    Write-Host ("FAILED (exit " + $LASTEXITCODE + ")")
    exit 1
}

Write-Host ("OK -> " + $out)
exit 0
