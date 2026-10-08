#requires -Version 7.4
<#
.SYNOPSIS
    同步源码后，一键构建 CFIT、更新依赖、发布并打包 PilotsDock。
.DESCRIPTION
    按 AppLogger -> AppTools -> SimConnectLib -> AppFramework -> Installer 的顺序
    构建 CFIT，以同一新版本生成本地 NuGet 包，随后更新全部直接 CFIT 引用并还原。
    发布 Plugin、ProfileManager、SimConnectHelper，核对 CFIT DLL，再生成安装器。
    不执行 git fetch/merge/commit/push，也不上传 Release。
.PARAMETER Version
    发布版本。默认读取源码 Plugin/manifest.json，避免沿用旧安装包版本。
.PARAMETER CfitRoot
    CFIT 源码目录。默认使用 PilotsDock 旁边的 CFIT。
.PARAMETER SkipCfitBuild
    使用 PackageRepo 已有的最新 CFIT 包，仍更新依赖、还原、发布和打包。
.EXAMPLE
    pwsh ./Release.ps1
.EXAMPLE
    pwsh ./Release.ps1 -Version 0.9.6.0 -SkipCfitBuild
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$CfitRoot = (Join-Path $PSScriptRoot '../CFIT'),
    [switch]$SkipCfitBuild,
    [string]$PluginOutput = 'Releases/com.mirabox.pilotsdock.sdPlugin'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseDependencies.ps1')
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$CfitRoot = (Resolve-Path -LiteralPath $CfitRoot).Path
$packageRepo = Join-Path $CfitRoot 'PackageRepo'
$manifestFile = Join-Path $repoRoot 'Plugin/manifest.json'
$versionFile = Join-Path $repoRoot 'Installer/Payload/version.json'
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if (-not $Version) { $Version = $manifest.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Version 必须为 x.y.z.w。' }
[void][version]$Version
# PackageApp.ps1 的发布路径固定；在任何清理操作之前验证绝对路径。
$outDir = [IO.Path]::GetFullPath((Join-Path $repoRoot $PluginOutput))
$expectedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Releases/com.mirabox.pilotsdock.sdPlugin'))
if ($outDir -ne $expectedOutput) { throw 'PluginOutput 必须为 Releases/com.mirabox.pilotsdock.sdPlugin（安装器打包路径）。' }
$sevenZip = 'C:\Program Files\7-Zip\7z.exe'
$nugetCli = Join-Path $repoRoot 'nuget.exe'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
foreach ($file in $sevenZip, $nugetCli, $vswhere, (Join-Path $repoRoot 'LICENSE')) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "缺少文件：$file" }
}
[void](Get-Command dotnet -ErrorAction Stop)
[void](Get-Command pwsh -ErrorAction Stop)
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw '未找到 Visual Studio MSBuild。' }
$cfitProjects = 'AppLogger', 'AppTools', 'SimConnectLib', 'AppFramework', 'Installer'
foreach ($name in $cfitProjects) {
    $project = Join-Path $CfitRoot "$name/$name.csproj"
    if (-not (Test-Path -LiteralPath $project)) { throw "缺少 CFIT 项目：$project" }
    if (-not $SkipCfitBuild -and [IO.File]::ReadAllText($project) -notmatch 'SkipCfitBuildHooks') {
        throw "请先同步 CFIT 的 SkipCfitBuildHooks 支持：$project"
    }
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'build.lck')) { throw 'build.lck 存在，会跳过 PI 生成；请结束原构建后再发布。' }
[void][IO.Directory]::CreateDirectory($packageRepo)
$lockPath = Join-Path $repoRoot '.release.lck'
# CreateNew prevents concurrent release runs from overwriting the same output.
$lock = [IO.File]::Open($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$previousLocation = Get-Location
$previousPrepared = $env:PILOTSDOCK_CFIT_PREPARED
try {
    Set-Location -LiteralPath $repoRoot
    Write-Host "Repo: $repoRoot`nCFIT: $CfitRoot`nVersion: $Version" -ForegroundColor Cyan
    if (-not $SkipCfitBuild) {
        # Unique, monotonically increasing versions avoid reusing NuGet's cached packages.
        $now = Get-Date
        $cfitVersion = [version]("{0}.{1}.{2}.{3}" -f $now.Year, $now.DayOfYear, $now.Hour, ($now.Minute * 60 + $now.Second))
        $latest = @(Get-ChildItem -LiteralPath $packageRepo -File | ForEach-Object {
            if ($_.Name -match '^CFIT\.[^.]+\.(\d+\.\d+\.\d+\.\d+)\.nupkg$') { [version]$Matches[1] }
        } | Sort-Object -Descending | Select-Object -First 1)
        if ($latest.Count -and $cfitVersion -le $latest[0]) {
            $v = $latest[0]
            $cfitVersion = [version]::new($v.Major, $v.Minor, $v.Build, $v.Revision + 1)
        }
        Write-Host "`n=== 构建 CFIT 本地包 $cfitVersion ===" -ForegroundColor Green
        $packages = @{}
        foreach ($name in $cfitProjects) {
            $project = Join-Path $CfitRoot "$name/$name.csproj"
            Set-CfitReferences $project $packages
            # Explicit pack owns ordering/versioning; disable CFIT's recursive build/pack hooks.
            Push-Location -LiteralPath (Split-Path -Parent $project)
            try {
                Invoke-ReleaseCommand dotnet @('pack', $project, '-c', 'Release', '-r', 'win-x64', '-o', $packageRepo,
                    "-p:Version=$cfitVersion", "-p:PackageVersion=$cfitVersion", "-p:AssemblyVersion=$cfitVersion",
                    "-p:FileVersion=$cfitVersion", '-p:SkipCfitBuildHooks=true',
                    "-p:RestoreAdditionalProjectSources=$packageRepo", '--nologo', '-v', 'minimal')
            } finally { Pop-Location }
            $id = "CFIT.$name"
            $package = Join-Path $packageRepo "$id.$cfitVersion.nupkg"
            if (-not (Test-Path -LiteralPath $package)) { throw "未生成 CFIT 包：$package" }
            $packages[$id] = [pscustomobject]@{ Id = $id; Version = $cfitVersion; Path = $package }
        }
        Assert-CfitPackageDependencies $packages
    } else {
        $packages = Get-CfitPackages $packageRepo
    }
    Write-Host "`n=== 统一 CFIT 引用并还原依赖 ===" -ForegroundColor Green
    foreach ($name in $cfitProjects) { Set-CfitReferences (Join-Path $CfitRoot "$name/$name.csproj") $packages }
    foreach ($name in 'Plugin', 'ProfileManager') { Set-CfitReferences (Join-Path $repoRoot "$name/$name.csproj") $packages }
    Set-InstallerCfitReferences $repoRoot $packages
    # Keep the managed/native SDK pair in Plugin in sync with the CFIT sources.
    foreach ($name in 'Microsoft.FlightSimulator.SimConnect.dll', 'SimConnect.dll') {
        Copy-Item -LiteralPath (Join-Path $CfitRoot "SimConnectLib/$name") -Destination (Join-Path $repoRoot "Plugin/$name") -Force
    }
    $projects = @('Plugin/Plugin.csproj', 'ProfileManager/ProfileManager.csproj', 'SimConnectHelper/SimConnectHelper.csproj')
    foreach ($project in $projects) {
        Invoke-ReleaseCommand dotnet @('restore', (Join-Path $repoRoot $project), '-r', 'win-x64',
            "-p:RestoreAdditionalProjectSources=$packageRepo", '--nologo', '-v', 'minimal')
    }
    Invoke-ReleaseCommand $nugetCli @('restore', (Join-Path $repoRoot 'Installer/packages.config'),
        '-PackagesDirectory', (Join-Path $repoRoot 'packages'), '-Source', "$packageRepo;https://api.nuget.org/v3/index.json",
        '-NonInteractive', '-Verbosity', 'quiet')
    $Timestamp = [DateTime]::UtcNow.ToString('yyyy.MM.dd.HHmm')
    [IO.File]::WriteAllText($versionFile, (@{ Version = $Version; Timestamp = $Timestamp } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $content = [IO.File]::ReadAllText($manifestFile)
    $content = [regex]::Replace($content, '("Version"\s*:\s*")[^"]+', { param($m) $m.Groups[1].Value + $Version })
    [IO.File]::WriteAllText($manifestFile, $content, [Text.UTF8Encoding]::new($false))
    # The output was resolved/checked against the one allowed directory above.
    if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
    $env:PILOTSDOCK_CFIT_PREPARED = '1'
    foreach ($project in $projects) {
        Write-Host "`n=== publish $project ===" -ForegroundColor Green
        Invoke-ReleaseCommand dotnet @('publish', (Join-Path $repoRoot $project), '-c', 'Release', '-r', 'win-x64',
            '-o', $outDir, '--no-restore', "-p:Version=$Version", "-p:FileVersion=$Version",
            "-p:AssemblyVersion=$Version", "-p:InformationalVersion=$Version+build$Timestamp",
            "-p:SolutionDir=$repoRoot\", '--nologo', '-v', 'minimal')
    }
    Get-ChildItem -LiteralPath $outDir -Recurse -Filter '*.pdb' | Remove-Item -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $outDir 'LICENSE') -Force
    Assert-CfitRelease $outDir $packages
    foreach ($name in 'Microsoft.FlightSimulator.SimConnect.dll', 'SimConnect.dll') {
        $expected = (Get-FileHash -LiteralPath (Join-Path $CfitRoot "SimConnectLib/$name")).Hash
        $actual = (Get-FileHash -LiteralPath (Join-Path $outDir $name)).Hash
        if ($expected -ne $actual) { throw "发布目录中的 SimConnect SDK 文件不一致：$name" }
    }
    Write-Host "`n=== 编译 Installer (Release/x64) ===" -ForegroundColor Green
    Invoke-ReleaseCommand $msbuild @((Join-Path $repoRoot 'Installer/Installer.csproj'), '-t:Rebuild',
        '-p:Configuration=Release', '-p:Platform=x64', "-p:SolutionDir=$repoRoot\", '-nologo', '-v:minimal')
    Assert-CfitRelease $outDir $packages (Join-Path $repoRoot 'Installer/Payload/AppPackage.zip')
    $installer = Join-Path $repoRoot 'PilotsDock-Installer-latest.exe'
    $info = (Get-Item -LiteralPath $installer).VersionInfo
    if ($info.FileVersion -ne $Version) { throw "安装器版本不一致：$($info.FileVersion)，预期 $Version" }
    Write-Host "`n完成：$installer`n版本：$Version ($Timestamp)" -ForegroundColor Green
    foreach ($id in ($packages.Keys | Sort-Object)) { Write-Host "$id : $($packages[$id].Version)" }
} finally {
    $env:PILOTSDOCK_CFIT_PREPARED = $previousPrepared
    Set-Location -LiteralPath $previousLocation.Path
    $lock.Dispose()
    Remove-Item -LiteralPath $lockPath -Force
}
