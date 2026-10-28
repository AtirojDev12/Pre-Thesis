param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/item-impact'
$projectPath = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force -Path $projectPath | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}
$settingsFolder = Join-Path $projectPath 'UserSettings'
New-Item -ItemType Directory -Force -Path $settingsFolder | Out-Null
$searchSettings = Join-Path $settingsFolder 'Search.settings'
if (Test-Path -LiteralPath $searchSettings) {
    $settings = Get-Content -Raw -LiteralPath $searchSettings
    Set-Content -LiteralPath $searchSettings -Value ($settings.Replace('indexOnEditorStartup = true', 'indexOnEditorStartup = false'))
} else {
    Set-Content -LiteralPath $searchSettings -Value 'indexOnEditorStartup = false'
}
$logPath = Join-Path $testRoot 'verify.log'
$resultPath = Join-Path $projectPath 'item-impact-results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod ItemImpactAuthoring.VerifyBatch -logFile "{1}"' -f $projectPath, $logPath
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(300000)) {
    Stop-Process -Id $process.Id
    throw "Unity checks exceeded five minutes. See $logPath"
}
if (!(Test-Path -LiteralPath $resultPath)) { throw "Unity did not write results. See $logPath" }
$results = Get-Content -LiteralPath $resultPath
$results
if ($process.ExitCode -ne 0 -or ($results -match '^FAIL ') -or ($results -match 'pending')) { throw "Item impact checks failed. See $logPath" }
