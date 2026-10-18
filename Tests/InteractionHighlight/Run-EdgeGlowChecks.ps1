param([Parameter(Mandatory = $true)][string] $UnityPath)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $repoRoot '.utmp/edge-glow'
$projectPath = Join-Path $testRoot 'Project'
foreach ($folder in @('Assets/Editor', 'Assets/Resources', 'Assets/Maker', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Force (Join-Path $projectPath $folder) | Out-Null
}
$cachePackages = @{}
Get-ChildItem (Join-Path $repoRoot 'Library/PackageCache') -Directory | ForEach-Object {
    $packageFile = Join-Path $_.FullName 'package.json'
    if (Test-Path $packageFile) {
        $info = Get-Content $packageFile -Raw | ConvertFrom-Json
        $cachePackages[$info.name] = @{ Path = $_.FullName; Info = $info }
    }
}
$testDependencies = @{}
function Add-TestDependency([string] $packageName) {
    if ($testDependencies.ContainsKey($packageName)) { return }
    if ($packageName.StartsWith('com.unity.modules.')) { $testDependencies[$packageName] = '1.0.0'; return }
    if (!$cachePackages.ContainsKey($packageName)) { throw "Open the main project once to cache $packageName" }
    $cached = $cachePackages[$packageName]
    $testDependencies[$packageName] = 'file:' + $cached.Path.Replace('\', '/')
    foreach ($dep in $cached.Info.dependencies.PSObject.Properties) { Add-TestDependency $dep.Name }
}
foreach ($package in @('com.unity.render-pipelines.universal', 'com.unity.modules.physics', 'com.unity.modules.imageconversion', 'com.unity.modules.animation')) {
    Add-TestDependency $package
}
@{ dependencies = $testDependencies } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $projectPath 'Packages/manifest.json')
Copy-Item (Join-Path $repoRoot 'ProjectSettings/ProjectVersion.txt') (Join-Path $projectPath 'ProjectSettings/')
foreach ($script in @('InteractionOutline', 'InteractionEdgeGlowFeature')) {
    Copy-Item (Join-Path $repoRoot "Assets/_Script/Interactive/$script.cs*") (Join-Path $projectPath 'Assets/')
}
foreach ($shader in @('InteractionOutline', 'InteractionEdgeGlow')) {
    Copy-Item (Join-Path $repoRoot "Assets/Resources/$shader.shader*") (Join-Path $projectPath 'Assets/Resources/')
}
foreach ($renderer in @('PC_Renderer', 'Mobile_Renderer')) {
    Copy-Item (Join-Path $repoRoot "Assets/Settings/$renderer.asset*") (Join-Path $projectPath 'Assets/')
}
Copy-Item (Join-Path $repoRoot 'Assets/RetroHorrorFilterFeature.cs*') (Join-Path $projectPath 'Assets/')
Copy-Item -LiteralPath (Join-Path $repoRoot 'Assets/RetroHorrorFilter (1).shader') -Destination (Join-Path $projectPath 'Assets/')
Copy-Item -LiteralPath (Join-Path $repoRoot 'Assets/RetroHorrorFilter (1).shader.meta') -Destination (Join-Path $projectPath 'Assets/')
Copy-Item (Join-Path $PSScriptRoot 'EdgeGlowChecks.cs') (Join-Path $projectPath 'Assets/Editor/')
foreach ($asset in @('Assets/Prefab/PrefabModel/Popcorn_Maker.prefab*', 'Assets/ModelThesis/Model_H_T/Popcorn_Maker.fbx*', 'Assets/ModelThesis/Texture/Popcorn_Marker/*')) {
    Copy-Item (Join-Path $repoRoot $asset) (Join-Path $projectPath 'Assets/Maker/')
}
$resultPath = Join-Path $testRoot 'results.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $testRoot 'unity-verified.log'
$arguments = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod EdgeGlowChecks.Run -logFile "{1}"' -f $projectPath, $logPath
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(300000)) {
    Stop-Process -Id $process.Id
    throw "Unity checks exceeded five minutes. See $logPath"
}
if (!(Test-Path -LiteralPath $resultPath)) { throw "No rendering results. See $logPath" }
$results = Get-Content -LiteralPath $resultPath
$results
if ($process.ExitCode -ne 0 -or ($results -match '^FAIL ')) { throw "Edge glow checks failed. See $logPath" }
