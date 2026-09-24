param(
    [string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe',
    [ValidateSet('d3d11', 'd3d12')] [string] $GraphicsApi = 'd3d12'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/customer-build'
$project = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $project | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings','Library/PackageCache')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $project $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editor = Join-Path $project 'Assets/Editor'
New-Item -ItemType Directory -Force $editor | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'CustomerBuildChecks.cs') $editor -Force
Copy-Item (Join-Path $PSScriptRoot 'CustomerBuildProbe.cs') (Join-Path $project 'Assets') -Force
$log = Join-Path $testRoot 'build.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod CustomerBuildChecks.Build -logFile "{1}"' -f $project,$log
$process = Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(900000)) { Stop-Process -Id $process.Id; throw "Build timed out: $log" }
if ($process.ExitCode -ne 0) { throw "Build failed: $log" }
$build = Join-Path $project 'CustomerBuild'
foreach ($mode in @('host','offline')) {
    $result = Join-Path $build "customer-results-$mode.txt"
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
    $playerLog = Join-Path $testRoot "$mode.log"
    $argsForPlayer = '-batchmode -force-{0} -screen-width 640 -screen-height 480 -logFile "{1}" {2}' -f $GraphicsApi, $playerLog, $(if ($mode -eq 'offline') { '--offline' } else { '' })
    $player = Start-Process (Join-Path $build 'CustomerChecks.exe') -ArgumentList $argsForPlayer -WindowStyle Hidden -PassThru
    if (!$player.WaitForExit(90000)) { Stop-Process -Id $player.Id; throw "Player timed out: $playerLog" }
    if (!(Test-Path -LiteralPath $result)) { throw "Player produced no results: $playerLog" }
    Get-Content $result
    if ($player.ExitCode -ne 0 -or (Select-String -LiteralPath $result -Pattern '^FAIL')) { throw "Player checks failed: $playerLog" }
}
