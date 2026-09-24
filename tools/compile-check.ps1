# Offline compile check for Warudo mod scripts.
#
# Compiles every .cs inside Mods/<ModName>/ against the real Warudo DLLs without
# starting Unity, so syntax and API mistakes (namespace shadowing, fields that
# collide with Node base members, ...) show up in seconds instead of minutes.
#
# It ALSO lints for the UMod code-security rules (see the "UMod sandbox lint" block
# below). That stage exists because the compile stage cannot see them: Roslyn is
# happy with `System.Reflection` / `value.GetType().Name`, and UMod's
# RunCodeValidation then kills the real build with
#   "Illegal reference to disallowed namespace: System.Reflection" -> BUILD FAILED!
# Two real builds were wasted that way (2026-09-25). Full report + the sanctioned
# replacement (DataOutputPort.ComputedValue) in HoUnityTools
# docs/pitfalls/BUILD_AND_TOOLING.md sections 4 and 4.1.
#
# NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 reads
# BOM-less files as ANSI, so non-ASCII comments here would break parsing.
#
# Usage:
#   powershell -File tools\compile-check.ps1
#   powershell -File tools\compile-check.ps1 -ManagedDir "D:\...\Warudo_Data\Managed"
#   powershell -File tools\compile-check.ps1 -LintOnly

param(
    [string]$ManagedDir = "D:\steam\steamapps\common\Warudo\Warudo_Data\Managed",
    [string]$CscPath    = "C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\csc.dll",
    [string[]]$ModsRoots = @('Mods', 'Mods-Ho'),
    [switch]$LintOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
# IMPORTANT: never write the produced DLL anywhere Unity imports. Unity ignores any
# folder whose name starts with ".", so ".compile-check" keeps the artifact invisible
# to the asset pipeline (otherwise the DLL would be imported as a plugin).
$outDir   = Join-Path $repoRoot ".compile-check"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not $LintOnly) {
    if (-not (Test-Path $ManagedDir)) { throw "Warudo Managed folder not found: $ManagedDir" }
    if (-not (Test-Path $CscPath))    { throw "Roslyn csc.dll not found: $CscPath (override with -CscPath)" }
}

# Only the assemblies a mod script actually needs.
# Node-only mods compile with the short list; [AssetType] mods (face trackers etc.)
# also need AnimationModule / JSONSerializeModule / UniTask / Warudo.Plugins.Core.
# Missing one shows up as CS0234/CS0246 -- just add it here.
# (The real UMod export compiles against the whole Managed folder.)
# NOTE: keep this file ASCII-only (PS 5.1 reads BOM-less files as ANSI).
$referenceNames = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.AnimationModule.dll',
    'UnityEngine.JSONSerializeModule.dll',
    # GUIUtility.systemCopyBuffer -- the only clipboard API reachable from a mod
    # (Warudo itself has none; see HoDebugLogNode's header). Re-added 2026-09-25
    # for the "copy" button, after having been dropped with the old clipboard button.
    'UnityEngine.IMGUIModule.dll',
    'UniTask.dll',
    'Warudo.Core.dll',
    'Warudo.Plugins.Core.dll',
    # UMod runtime: Plugin.ModHost gives ModHost, whose SharedAssets returns UMod.IModAssets
    # (Load<T>(nameOrPath) -- how a plugin mod loads its own packaged Unity assets).
    # Only UMod-ModTools is on the forbidden list; the runtime assemblies are the mod API.
    'UMod.dll',
    'UMod-Interface.dll'
)
$references = @()
foreach ($name in $referenceNames) {
    $references += "/reference:" + (Join-Path $ManagedDir $name)
}

$failed = 0
# ModsRoots lets a second workspace root (e.g. Mods-Ho) be checked with the same tool.
$mods = @()
foreach ($rootName in $ModsRoots) {
    $rootPath = Join-Path $repoRoot $rootName
    if (Test-Path $rootPath) { $mods += Get-ChildItem $rootPath -Directory }
}

# ---------------------------------------------------------------------------
# UMod sandbox lint.
#
# UMod's RunCodeValidation inspects the IL and rejects anything that touches
# System.Reflection -- including *indirect* touches, which is the trap:
# `value.GetType().Name` looks innocent but compiles to
# `callvirt System.Reflection.MemberInfo::get_Name` and fails the build.
# Roslyn does not care, so the compile stage below cannot catch it.
#
# Proven-banned (two real builds, 2026-09-25):
#   System.Reflection namespace            -> Illegal reference to disallowed namespace
#   System.Reflection.* types              -> Illegal reference to disallowed type
#   members declared in those types        -> Indirect illegal reference via type exclusion
#
# Add `lint-allow` on a line to skip it (e.g. a comment you want to keep).
# Comments and string literals are stripped before matching, so talking *about*
# the ban (like this block, or a Chinese comment) is fine.
# ---------------------------------------------------------------------------
$bannedPatterns = [ordered]@{
    'System\.Reflection'                 = 'the System.Reflection namespace'
    '\bBindingFlags\b'                   = 'System.Reflection.BindingFlags'
    '\bMemberInfo\b'                     = 'System.Reflection.MemberInfo'
    '\bMethodInfo\b'                     = 'System.Reflection.MethodInfo'
    '\bMethodBase\b'                     = 'System.Reflection.MethodBase'
    '\bConstructorInfo\b'                = 'System.Reflection.ConstructorInfo'
    '\bFieldInfo\b'                      = 'System.Reflection.FieldInfo'
    '\bPropertyInfo\b'                   = 'System.Reflection.PropertyInfo'
    '\bEventInfo\b'                      = 'System.Reflection.EventInfo'
    '\bParameterInfo\b'                  = 'System.Reflection.ParameterInfo'
    '\bCustomAttributeData\b'            = 'System.Reflection.CustomAttributeData'
    '\bAssemblyName\b'                   = 'System.Reflection.AssemblyName'
    '\.GetType\s*\('                     = 'Object.GetType(); using its result hits MemberInfo'
    '\.GetMethod\s*\('                   = 'Type.GetMethod'
    '\.GetMethods\s*\('                  = 'Type.GetMethods'
    '\.GetField\s*\('                    = 'Type.GetField'
    '\.GetFields\s*\('                   = 'Type.GetFields'
    '\.GetProperty\s*\('                 = 'Type.GetProperty'
    '\.GetProperties\s*\('               = 'Type.GetProperties'
    '\.GetMember\s*\('                   = 'Type.GetMember'
    '\.GetMembers\s*\('                  = 'Type.GetMembers'
    '\.GetConstructor\s*\('              = 'Type.GetConstructor'
    '\.GetCustomAttribute'               = 'MemberInfo.GetCustomAttribute*'
    '\.GetCustomAttributes'              = 'MemberInfo.GetCustomAttributes*'
    'DllImport'                          = 'P/Invoke (Illegal PInvoke References)'
    '\bMarshal\s*\.'                     = 'System.Runtime.InteropServices.Marshal'
    'Delegate\.CreateDelegate'           = 'Delegate.CreateDelegate'
    'System\.Linq\.Expressions'          = 'expression trees (reflection-adjacent; unverified)'
}

# Strip comments + string literals so only real code is matched, while keeping the
# line count identical (so reported line numbers stay right).
function Get-CodeOnly([string]$text) {
    $noBlock = [regex]::Replace($text, '(?s)/\*.*?\*/', {
        param($m) ($m.Value -replace '[^\r\n]', ' ')
    })
    $noLine = [regex]::Replace($noBlock, '//[^\r\n]*', ' ')
    $noString = [regex]::Replace($noLine, '@?"(\\.|[^"\\\r\n])*"', '""')
    return $noString
}

$lintHits = New-Object System.Collections.ArrayList
foreach ($mod in $mods) {
    $files = @(Get-ChildItem $mod.FullName -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
    foreach ($file in $files) {
        $lines = (Get-CodeOnly ([System.IO.File]::ReadAllText($file))) -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -like '*lint-allow*') { continue }
            foreach ($pattern in $bannedPatterns.Keys) {
                if ([regex]::IsMatch($line, $pattern)) {
                    $rel = $file.Substring($repoRoot.Length + 1)
                    [void]$lintHits.Add(("    " + $rel + ":" + ($i + 1) + "  /" + $pattern + "/  ->  " + $bannedPatterns[$pattern]))
                    break
                }
            }
        }
    }
}

if ($lintHits.Count -gt 0) {
    Write-Host ""
    Write-Host ("=== UMod sandbox lint: " + $lintHits.Count + " hit(s) -- the real build WILL fail")
    foreach ($hit in $lintHits) { Write-Host $hit }
    Write-Host ""
    Write-Host "    These are rejected at UMod's RunCodeValidation stage (not by Roslyn)."
    Write-Host "    Reading another node's output port without reflection: use"
    Write-Host "    connection.OutputPort / Node.GetDataOutputPort(key) and call ComputedValue()."
    Write-Host "    See HoUnityTools docs/pitfalls/BUILD_AND_TOOLING.md sections 4 / 4.1."
    Write-Host ""
    exit 1
}
Write-Host "UMod sandbox lint: clean"
if ($LintOnly) { exit 0 }

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
