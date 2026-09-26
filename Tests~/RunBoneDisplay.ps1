param(
    [Parameter(Mandatory=$true)][string] $Project,
    [string] $Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not (Test-Path -LiteralPath (Join-Path $Project '.ho-face-validation'))) {
    throw 'Disposable validation project marker missing.'
}
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $Project 'Assets\Editor\BoneDisplay'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'Mods-Ho\HoFaceTracking\Core\HoFaceBoneDisplay.cs') -Destination $target
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BoneDisplayValidation.cs') -Destination $target
$log = Join-Path $Project 'bone-display-validation.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $Project + '"'),
    '-executeMethod', 'BoneDisplayValidation.RunBatch', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath $log | Select-String 'BONE_DISPLAY|error CS|Exception:'
if ($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -Pattern 'BONE_DISPLAY_PASS' -Quiet)) {
    throw "Validation failed; see $log"
}
