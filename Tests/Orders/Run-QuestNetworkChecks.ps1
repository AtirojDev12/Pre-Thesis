param(
    [string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe',
    [string] $TestRoot = '',
    [switch] $SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$TestRoot) { $TestRoot = Join-Path $repoRoot '.utmp/quest-network' }
$TestRoot = [IO.Path]::GetFullPath($TestRoot)
$projectPath = Join-Path $TestRoot 'Project'
if (!$SkipBuild) {
    New-Item -ItemType Directory -Force $projectPath | Out-Null
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Library/PackageCache')) {
        $source = Join-Path $repoRoot $folder
        if (!(Test-Path -LiteralPath $source)) { continue }
        & robocopy $source (Join-Path $projectPath $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
    }
    New-Item -ItemType Directory -Force (Join-Path $projectPath 'Assets/Editor') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QuestNetworkProbe.cs') -Destination (Join-Path $projectPath 'Assets') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QuestNetworkBuild.cs') -Destination (Join-Path $projectPath 'Assets/Editor') -Force
    $buildLog = Join-Path $TestRoot 'quest-network-build.log'
    $buildArgs = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod QuestNetworkBuild.Build -logFile "{1}"' -f $projectPath, $buildLog
    $builder = Start-Process $UnityPath -ArgumentList $buildArgs -WindowStyle Hidden -PassThru
    if (!$builder.WaitForExit(600000)) { Stop-Process -Id $builder.Id; throw "Build timed out: $buildLog" }
    if ($builder.ExitCode -ne 0) { throw "Build failed: $buildLog" }
}
$executable = Join-Path $projectPath 'QuestNetworkBuild/QuestNetworkChecks.exe'
if (!(Test-Path -LiteralPath $executable)) { throw "Missing test build: $executable" }
$outputPath = Join-Path $TestRoot ('quest-network-run-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $outputPath | Out-Null
$players = [Collections.Generic.List[Diagnostics.Process]]::new()
function Start-QuestPlayer([string] $Role) {
    $playerLog = Join-Path $outputPath ($Role + '.log')
    $playerArgs = '-batchmode -nographics -job-worker-count 2 -screen-width 640 -screen-height 480 -logFile "{0}" --quest-role {1} --quest-output "{2}"' -f $playerLog, $Role, $outputPath
    $process = Start-Process $executable -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
    $players.Add($process)
    return $process
}
function Wait-QuestMarker([string] $Name, [int] $Seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (!(Test-Path -LiteralPath (Join-Path $outputPath ($Name + '.txt')))) {
        foreach ($process in $players) { if ($process.HasExited) { throw "Player exited before $Name; results: $outputPath" } }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for $Name; results: $outputPath" }
        Start-Sleep -Milliseconds 200
    }
}
try {
    $hostPlayer = Start-QuestPlayer 'host'
    Wait-QuestMarker 'listening' 40
    $clientPlayer = Start-QuestPlayer 'client'
    Wait-QuestMarker 'launch-late' 150
    $latePlayer = Start-QuestPlayer 'late'
    $deadline = [DateTime]::UtcNow.AddSeconds(150)
    while ($players | Where-Object { !$_.HasExited }) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Players timed out; results: $outputPath" }
        Start-Sleep -Milliseconds 200
    }
    foreach ($role in @('host', 'client', 'late')) {
        $resultPath = Join-Path $outputPath ('results-' + $role + '.txt')
        if (!(Test-Path -LiteralPath $resultPath)) { throw "Missing $role results: $outputPath" }
        $results = Get-Content -LiteralPath $resultPath
        Write-Output "$role passed: $(($results | Where-Object { $_ -match '^PASS ' }).Count)"
        $results
        if ($results -match '^FAIL ') { throw "$role failed: $outputPath" }
    }
    if ($players | Where-Object { $_.ExitCode -ne 0 }) { throw "Player failed: $outputPath" }
    Write-Output "Network results: $outputPath"
}
finally {
    foreach ($process in $players) { if (!$process.HasExited) { Stop-Process -Id $process.Id } }
}
