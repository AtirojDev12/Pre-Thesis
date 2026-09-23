param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/ticket-regression'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $projectPath | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}
$editorPath = Join-Path $projectPath 'Assets/Editor'
New-Item -ItemType Directory -Force $editorPath | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'TicketRegressionRunner.cs') $editorPath -Force
$logPath = Join-Path $testRoot 'unity.log'
$resultPath = Join-Path $projectPath 'ticket-results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod TicketRegressionRunner.Build -logFile "{1}"' -f $projectPath, $logPath
$process = Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(600000)) { Stop-Process -Id $process.Id; throw "Unity checks timed out. See $logPath" }
if (!(Test-Path -LiteralPath $resultPath)) { throw "Unity did not produce results. See $logPath" }
$results = Get-Content -LiteralPath $resultPath
$results
if ($process.ExitCode -ne 0 -or ($results -match '^FAIL ')) { throw "Ticket checks failed. See $logPath" }
