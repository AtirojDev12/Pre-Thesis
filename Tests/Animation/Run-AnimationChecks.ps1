param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $repo '.utmp/customer-build/Project'
New-Item -ItemType Directory -Force (Join-Path $project 'Assets/Editor') | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings','Library/PackageCache')) {
    & robocopy (Join-Path $repo $folder) (Join-Path $project $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
Copy-Item (Join-Path $PSScriptRoot 'PlayerAnimationProbe.cs') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $PSScriptRoot 'PlayerAnimationBuild.cs') (Join-Path $project 'Assets/Editor') -Force
$log = Join-Path $repo '.utmp/animation-build.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod PlayerAnimationBuild.Build -logFile "{1}"' -f $project,$log
$process = Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(900000)) { Stop-Process -Id $process.Id; throw 'Build timed out' }
if ($process.ExitCode -ne 0) { throw "Build failed: $log" }
$build = Join-Path $project 'AnimationBuild'
foreach ($mode in @('host','client')) {
    $result = Join-Path $build "animation-results-$mode.txt"
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
}
$hostLog = Join-Path $repo '.utmp/animation-host.log'
$clientLog = Join-Path $repo '.utmp/animation-client.log'
$hostProcess = Start-Process (Join-Path $build 'AnimationChecks.exe') -ArgumentList ('-batchmode -force-d3d11 -logFile "{0}"' -f $hostLog) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 2
$clientProcess = Start-Process (Join-Path $build 'AnimationChecks.exe') -ArgumentList ('-batchmode -force-d3d11 --client -logFile "{0}"' -f $clientLog) -WindowStyle Hidden -PassThru
foreach ($process in @($hostProcess, $clientProcess)) {
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw 'Player timed out' }
}
foreach ($mode in @('host','client')) { Get-Content (Join-Path $build "animation-results-$mode.txt") }
if ($hostProcess.ExitCode -ne 0 -or $clientProcess.ExitCode -ne 0) { throw 'Animation checks failed' }
