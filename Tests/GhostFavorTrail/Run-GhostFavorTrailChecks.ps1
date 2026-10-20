param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# Reuse the existing isolated regression checkout rather than duplicate the game's large models.
$testRoot = Join-Path $repoRoot '.utmp/popcorn-regression'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $projectPath | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}
$editorPath = Join-Path $projectPath 'Assets/Editor'
New-Item -ItemType Directory -Force $editorPath | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GhostFavorTrailChecks.cs') -Destination $editorPath -Force
$resultPath = Join-Path $testRoot 'ghost-trail-results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $testRoot 'ghost-trail-unity.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod GhostFavorTrailChecks.Run -logFile "{1}"' -f $projectPath, $logPath
$process = Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(240000)) { Stop-Process -Id $process.Id; throw "Trail checks timed out. See $logPath" }
if (!(Test-Path -LiteralPath $resultPath)) { throw "Unity produced no trail results. See $logPath" }
Get-Content -LiteralPath $resultPath
if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $resultPath -Pattern '^FAIL ' -Quiet)) { throw "Trail checks failed. See $logPath" }
