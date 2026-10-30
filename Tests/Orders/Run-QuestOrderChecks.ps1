param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/quest-orders'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $projectPath | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
    $source = Join-Path $repoRoot $folder
    if (!(Test-Path -LiteralPath $source)) { continue }
    & robocopy $source (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}
$editorPath = Join-Path $projectPath 'Assets/Editor'
New-Item -ItemType Directory -Force $editorPath | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QuestOrderChecks.cs') -Destination $editorPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QuestCustomerPlayChecks.cs') -Destination $editorPath -Force
foreach ($suite in @(@('QuestOrderChecks.Run', 'quest-order-results.txt'), @('QuestCustomerPlayChecks.Run', 'quest-customer-play-results.txt'))) {
    $resultPath = Join-Path $projectPath $suite[1]
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
    $logPath = Join-Path $testRoot ($suite[0] + '.log')
    $arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod {1} -logFile "{2}"' -f $projectPath, $suite[0], $logPath
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(300000)) { Stop-Process -Id $process.Id; throw "Unity checks timed out: $logPath" }
    if (!(Test-Path -LiteralPath $resultPath)) { throw "Unity produced no results: $logPath" }
    $results = Get-Content -LiteralPath $resultPath
    $results
    if ($process.ExitCode -ne 0 -or ($results -match '^FAIL ')) { throw "Quest checks failed: $logPath" }
}
