param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/popcorn-regression'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $projectPath | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}
New-Item -ItemType Directory -Force (Join-Path $projectPath 'Assets/Editor') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PaperReaderChecks.cs') -Destination (Join-Path $projectPath 'Assets/Editor/') -Force
$resultPath = Join-Path $testRoot 'paper-reader-results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $testRoot 'paper-reader-unity.log'
$arguments = '-batchmode -nographics -projectPath "{0}" -executeMethod PaperReaderChecks.Run -logFile "{1}"' -f $projectPath, $logPath
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
while (!$process.WaitForExit(1000)) {
    if ((Get-Date) - $process.StartTime -gt [TimeSpan]::FromMinutes(5)) {
        Stop-Process -Id $process.Id
        throw "Paper reader checks timed out. See $logPath"
    }
}
if (!(Test-Path -LiteralPath $resultPath)) { throw "Unity produced no results. See $logPath" }
Get-Content -LiteralPath $resultPath
if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $resultPath -Pattern '^FAIL ' -Quiet)) {
    throw "Paper reader checks failed. See $logPath"
}
