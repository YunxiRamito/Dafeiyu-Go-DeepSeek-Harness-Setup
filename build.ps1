<#
  build.ps1 —— 编译 DSH Installer(开发期"只编译,不合成")
  产物: dist\DSH-Installer.exe (WinUI3 主程序,框架依赖,不带运行库)
        dist\Boot.exe           (引导,.NET Framework,负责按需下载安装运行库)
        dist\DSH-Uninstall.exe  (独立卸载程序)
        dist\payload\launcher.zip (DSH 启动器,从 DeepSeek Starter 的构建产物打包)

  要发给用户的**单个 Setup.exe** 请用 pack-release.ps1 —— 它在这些产物之上
  追加 payload 并合成自解压包。
#>
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [switch]$SkipLauncher,
    [switch]$SkipBoot
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$dotnet = 'G:\DeepSeek DSH\.tools\dotnet\dotnet.exe'
$nuget = 'G:\DeepSeek DSH\.nuget-packages'
$uiProject = Join-Path $PSScriptRoot 'src\DshInstaller\DshInstaller.csproj'
$sharedProject = Join-Path $PSScriptRoot 'src\DshInstaller.Shared\DshInstaller.Shared.csproj'

# 引导程序的源码在仓库顶层 boot\ 目录 —— 它不是 .NET 工程,是 csc 直接编的单文件。
# (以前这里指向 src\DshInstaller.Boot\,那个目录现在是空的,别再指过去。)
$bootSource = Join-Path $PSScriptRoot 'boot\Boot.cs'
$uninstallSource = Join-Path $PSScriptRoot 'boot\Uninstall.cs'

$frameworkCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$iconPath = Join-Path $PSScriptRoot 'assets\DSHInstaller.ico'
$launcherRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'Dafeiyu-Go-DeepSeek-Harness-Click-To-Run'
$launcherProject = Join-Path $launcherRoot 'source\DeepSeekHarness.csproj'
$launcherVersion = '1.4.9'
if (Test-Path -LiteralPath $launcherProject) {
    $launcherProjectText = Get-Content -LiteralPath $launcherProject -Raw -Encoding UTF8
    $launcherVersionMatch = [regex]::Match($launcherProjectText, '<Version>([^<]+)</Version>')
    if ($launcherVersionMatch.Success) {
        $launcherVersion = $launcherVersionMatch.Groups[1].Value.Trim()
    }
}
$launcherOut = Join-Path $launcherRoot ("source\dist-" + $launcherVersion)

if (-not (Test-Path -LiteralPath $dotnet)) { throw "未找到 .NET SDK: $dotnet" }

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$env:NUGET_PACKAGES = $nuget

Write-Host '[1/4] 编译共享库...' -ForegroundColor Cyan
& $dotnet build $sharedProject -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "共享库编译失败: $LASTEXITCODE" }

Write-Host '[2/4] 发布 WinUI3 主程序(框架依赖)...' -ForegroundColor Cyan
& $dotnet publish $uiProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $OutputDirectory `
    -p:PublishSingleFile=false `
    -p:WindowsAppSDKSelfContained=false `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "主程序发布失败: $LASTEXITCODE" }

if (-not $SkipBoot) {
    Write-Host '[3/4] 编译引导程序与独立卸载程序 (.NET Framework)...' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $frameworkCompiler)) { throw "未找到 csc.exe: $frameworkCompiler" }

    $compilerArgs = @('/nologo', '/platform:anycpu', '/optimize+', '/utf8output')
    if (Test-Path -LiteralPath $iconPath) { $compilerArgs += "/win32icon:$iconPath" }

    $bootPath = Join-Path $OutputDirectory 'Boot.exe'
    & $frameworkCompiler @compilerArgs /target:winexe /out:$bootPath `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll $bootSource
    if ($LASTEXITCODE -ne 0) { throw "引导程序编译失败: $LASTEXITCODE" }

    $uninstallPath = Join-Path $OutputDirectory 'DSH-Uninstall.exe'
    & $frameworkCompiler @compilerArgs /target:winexe /out:$uninstallPath `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll $uninstallSource
    if ($LASTEXITCODE -ne 0) { throw "卸载程序编译失败: $LASTEXITCODE" }
} else {
    Write-Host '[3/4] 跳过引导程序' -ForegroundColor DarkGray
}

if (-not $SkipLauncher) {
    Write-Host '[4/4] 打包启动器 payload...' -ForegroundColor Cyan
    $payloadDir = Join-Path $OutputDirectory 'payload'
    if (-not (Test-Path $payloadDir)) { New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null }
    $zip = Join-Path $payloadDir 'launcher.zip'
    if (Test-Path $launcherOut) {
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $launcherOut '*') -DestinationPath $zip -CompressionLevel Optimal
        Write-Host ("    launcher.zip: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
    } else {
        Write-Warning "找不到启动器构建产物: $launcherOut (先编译 DeepSeek Starter)"
    }
} else {
    Write-Host '[4/4] 跳过启动器打包' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '构建完成:' -ForegroundColor Green
Get-ChildItem $OutputDirectory -File | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
