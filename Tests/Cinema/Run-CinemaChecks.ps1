param([string] $UnityPath = 'C:/InstallUnity/6000.5.7f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/cinema-checks'
$project = Join-Path $testRoot 'Project'
New-Item -ItemType Directory -Force $project | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings','Library/PackageCache')) {
    & robocopy (Join-Path $repoRoot $folder) (Join-Path $project $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editor = Join-Path $project 'Assets/Editor'
New-Item -ItemType Directory -Force $editor | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'CinemaGameplayChecks.cs') $editor -Force
foreach ($mode in @('Offline','Host')) {
    $log = Join-Path $testRoot "$mode.log"
    $result = Join-Path $project ("cinema-" + $mode.ToLowerInvariant() + '-results.txt')
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
    $arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod CinemaGameplayChecks.Run{1} -logFile "{2}"' -f $project,$mode,$log
    $process = Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(600000)) { Stop-Process -Id $process.Id; throw "Timed out: $log" }
    if (!(Test-Path -LiteralPath $result)) { throw "No results: $log" }
    Get-Content $result
    if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $result -Pattern '^FAIL')) { throw "Checks failed: $log" }
}
