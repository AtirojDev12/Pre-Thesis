param([Parameter(Mandatory = $true)] [string] $UnityPath)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/throw-regression'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force -Path $projectPath | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
    $source = Join-Path $repoRoot $folder
    if (!(Test-Path -LiteralPath $source)) { continue }
    & robocopy $source (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}
$editorPath = Join-Path $projectPath 'Assets/Editor'
New-Item -ItemType Directory -Force -Path $editorPath | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThrowRegressionRunner.cs') -Destination $editorPath -Force
$logPath = Join-Path $testRoot 'unity.log'
$resultPath = Join-Path $projectPath 'throw-results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod ThrowRegressionRunner.Run -logFile "{1}"' -f $projectPath, $logPath
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(300000)) {
    Stop-Process -Id $process.Id
    throw "Unity checks exceeded five minutes. See $logPath"
}
if (!(Test-Path -LiteralPath $resultPath)) { throw "Unity did not write results. See $logPath" }
$results = Get-Content -LiteralPath $resultPath
$results
if ($process.ExitCode -ne 0 -or ($results -match '^FAIL ')) { throw "Throw checks failed. See $logPath" }
