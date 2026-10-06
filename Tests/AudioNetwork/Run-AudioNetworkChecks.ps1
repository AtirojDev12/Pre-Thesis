param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/audio-network'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $projectPath | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings','Library/PackageCache')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorPath = Join-Path $projectPath 'Assets/Editor'
New-Item -ItemType Directory -Force $editorPath | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'AudioNetworkChecks.cs') $editorPath -Force
$resultPath = Join-Path $projectPath 'audio-network-results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $testRoot 'unity.log'
$arguments = '-batchmode -nographics -projectPath "{0}" -executeMethod AudioNetworkChecks.Run -logFile "{1}"' -f $projectPath,$logPath
$process = Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(600000)) { Stop-Process -Id $process.Id; throw "Unity checks timed out: $logPath" }
if (!(Test-Path -LiteralPath $resultPath)) { throw "No results: $logPath" }
Get-Content -LiteralPath $resultPath
if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $resultPath -Pattern '^FAIL')) { throw "Audio checks failed: $logPath" }
