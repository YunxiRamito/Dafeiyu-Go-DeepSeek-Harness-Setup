# 正式打包:框架依赖发布 -> 补 XAML 资源 -> 编引导/卸载器 -> 追加 payload -> 单个 Setup.exe
#
# 用法:
#   .\pack-release.ps1                 打包并放到桌面
#   .\pack-release.ps1 -NoDesktop      只在 dist 里产出,不拷桌面
#
# 产物:
#   dist\DSH-Installer-Setup.exe   单文件安装包(用户拿到的就是这个)
#   dist\DSH-Uninstall.exe         独立卸载程序(会被安装器铺到目标机器上)
#   dist\payload.zip               安装器本体(自解压用的那一段)
#   dist\selfcontained\            发布出来的安装器目录(调试用)
#
# 为什么**不再自包含**:
#   自包含要把 .NET 运行时和 Windows App Runtime 一起塞进来,包一下子涨到 119 MB。
#   而现在这两个运行库由引导程序在装的时候**按需在线下载并静默安装**(见 boot\Boot.cs),
#   所以安装包只有十几 MB —— 用户不用为了装个 DSH 先下完一百多 MB 的运行时。
#
# 为什么不打包成真正的单个可执行文件:
#   安装器是未打包的 WinUI3 程序,一堆 dll + .xbf + .pri,没法真的编成一个 exe。
#   所以走"附加式自解压":引导程序 exe 后面直接接上 payload.zip,运行时按 EOCD 反推偏移解开。

param(
    [switch]$NoDesktop
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\DshInstaller\DshInstaller.csproj'
$bootDir = Join-Path $root 'boot'
$assets = Join-Path $root 'assets'
$dist = Join-Path $root 'dist'
$publishDir = Join-Path $dist 'selfcontained'
$payloadZip = Join-Path $dist 'payload.zip'
$bootExe = Join-Path $dist 'DSH-Installer-Setup.exe'
$uninstallExe = Join-Path $dist 'DSH-Uninstall.exe'
$setupExe = Join-Path $dist 'DSH-Installer-Setup.exe'
$icon = Join-Path $assets 'DSHInstaller.ico'
$versionInfoDir = Join-Path $dist '_versioninfo'

# 版本号与产品信息只有一个来源:Directory.Build.props
$propsPath = Join-Path $root 'Directory.Build.props'
$propsRaw = Get-Content $propsPath -Raw
function Get-Prop {
    param([string]$Name, [string]$Fallback = '')
    $m = [regex]::Match($propsRaw, "<$Name>([^<]+)</$Name>")
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return $Fallback
}

$version = Get-Prop 'InstallerVersion'
if (-not $version) { throw 'Directory.Build.props 里没读到 InstallerVersion' }

$productName = Get-Prop 'Product' 'DeepSeek Harness'
$companyName = Get-Prop 'Company' 'Deepseek / KitamaruRamito'
$copyright = Get-Prop 'Copyright'
$bootTitle = Get-Prop 'BootTitle' 'DeepSeek Harness 安装程序'
$bootDescription = Get-Prop 'BootDescription' 'DeepSeek Harness 的一体化 Windows 安装程序'
$uninstallTitle = Get-Prop 'UninstallTitle' 'DeepSeek Harness 卸载程序'
$uninstallDescription = Get-Prop 'UninstallDescription' '卸载 DeepSeek Harness 以及它装上的组件'

# 优先用本机那套便携 SDK;CI 上没有这个目录,就退回 PATH 里的 dotnet
$dotnet = 'G:\DeepSeek DSH\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# 本机有离线 NuGet 缓存就用它(省流量);CI 上没有这个目录,交给默认缓存
$localNuget = 'G:\DeepSeek DSH\.nuget-packages'
if (Test-Path -LiteralPath $localNuget) { $env:NUGET_PACKAGES = $localNuget }

if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc:$csc" }
if (-not (Test-Path -LiteralPath $icon)) {
    Write-Host '图标不存在,先跑一次 tools\make-icon.ps1' -ForegroundColor Yellow
    & (Join-Path $root 'tools\make-icon.ps1')
}

Write-Host '[1/7] 框架依赖发布(不带 .NET 运行时,运行库由引导程序按需装)' -ForegroundColor Cyan

# 先把占用输出目录的进程请走:进程活着的话 Remove-Item 会静默失败,
# 于是旧的 .xbf 留在原地和新的一起堆着 —— 界面改动"完全不生效"就是这么来的。
Get-Process | Where-Object { $_.ProcessName -like '*DSH-Installer*' -or $_.ProcessName -like '*DSH-Uninstall*' } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

for ($attempt = 1; $attempt -le 5; $attempt++) {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $publishDir)) { break }
    Start-Sleep -Milliseconds 400
}
if (Test-Path $publishDir) { throw "输出目录清不掉,可能有进程占着:$publishDir" }

& $dotnet publish $proj -c Release -r win-x64 --self-contained false `
    -p:WindowsAppSDKSelfContained=false -p:PublishSingleFile=false `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir --nologo -v minimal 2>&1 |
    Select-String -Pattern ': error' | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

Write-Host '[2/7] 补 .xbf / .pri(关键)' -ForegroundColor Cyan
$bin = Join-Path $root 'src\DshInstaller\bin\Release\net8.0-windows10.0.19041.0'
Get-ChildItem $bin -Recurse -Include '*.xbf', '*.pri' -ErrorAction SilentlyContinue | ForEach-Object {
    $rel = $_.FullName.Substring($bin.Length).TrimStart('\')
    $dst = Join-Path $publishDir $rel
    New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
    Copy-Item $_.FullName $dst -Force
}
$xbf = (Get-ChildItem $publishDir -Recurse -Filter '*.xbf' -ErrorAction SilentlyContinue | Measure-Object).Count
$pri = (Get-ChildItem $publishDir -Recurse -Filter '*.pri' -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host "  .xbf=$xbf  .pri=$pri"
if ($xbf -lt 10 -or $pri -lt 1) { throw "XAML 资源缺失(xbf=$xbf pri=$pri),打出来的包会运行不起来" }

# 去掉 publish 常见的多余子目录(和根目录资源重复,容易加载到旧的那份)
$stray = Join-Path $publishDir 'win-x64'
if (Test-Path $stray) { Remove-Item $stray -Recurse -Force }

Write-Host '[3/7] 校验主程序' -ForegroundColor Cyan
$exe = Join-Path $publishDir 'DSH-Installer.exe'
if (-not (Test-Path $exe)) { throw "没有生成 $exe" }
$sizeMb = [math]::Round(((Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "  发布输出 $sizeMb MB(框架依赖,所以只有这么大)"

Write-Host '[4/7] 编译引导程序与独立卸载程序(.NET Framework,无前置依赖)' -ForegroundColor Cyan

$compilerArgs = @('/nologo', '/platform:anycpu', '/optimize+', '/utf8output')
if (Test-Path $icon) { $compilerArgs += "/win32icon:$icon" }

# --- 版本资源 ---------------------------------------------------------------
#
# csc 编出来的 exe **默认没有版本资源** —— 属性里"文件版本/产品名称/版权"全是空的,
# "原始文件名"还会写着编译时的源文件名(用户看到过 Boot.exe 那一版)。
# 版本资源是从**程序集特性**生成的,所以这里按 Directory.Build.props 的版本号
# 现生成一份特性文件,和源码一起编。
#
# 文件必须带 BOM:csc 不看 BOM 就按系统 ANSI 读,中文会变成乱码。
New-Item -ItemType Directory -Path $versionInfoDir -Force | Out-Null

$fileVersion = ($version -split '\.')
while ($fileVersion.Count -lt 4) { $fileVersion += '0' }
$fileVersion = ($fileVersion[0..3] -join '.')

function New-VersionInfo {
    param([string]$Path, [string]$Title, [string]$Description)

    $body = @"
using System.Reflection;

[assembly: AssemblyTitle("$Title")]
[assembly: AssemblyDescription("$Description")]
[assembly: AssemblyProduct("$productName")]
[assembly: AssemblyCompany("$companyName")]
[assembly: AssemblyCopyright("$copyright")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$version")]
"@

    [System.IO.File]::WriteAllText($Path, $body, (New-Object System.Text.UTF8Encoding($true)))
}

$bootVersionFile = Join-Path $versionInfoDir 'BootVersionInfo.cs'
$uninstallVersionFile = Join-Path $versionInfoDir 'UninstallVersionInfo.cs'

New-VersionInfo -Path $bootVersionFile -Title $bootTitle -Description $bootDescription
New-VersionInfo -Path $uninstallVersionFile -Title $uninstallTitle -Description $uninstallDescription

# 引导程序**直接编译成最终文件名** —— PE 里的"原始文件名"取的是编译时的 /out 名字,
# 先编成 Boot.exe 再改名的话,属性里会一直写着 Boot.exe。
# 后面的步骤会读它的全部字节、再把 payload 追加到同一个文件里(先读后写,没问题)。
& $csc @compilerArgs /target:winexe /out:$bootExe `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $bootDir 'Boot.cs') $bootVersionFile 2>&1 | ForEach-Object { Write-Host "  $_" }
if (-not (Test-Path $bootExe)) { throw '引导程序编译失败' }
if ((Get-Item $bootExe).Length -lt 10240) { throw '引导程序小得不像话,编译多半没成功' }

& $csc @compilerArgs /target:winexe /out:$uninstallExe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $bootDir 'Uninstall.cs') $uninstallVersionFile 2>&1 | ForEach-Object { Write-Host "  $_" }
if (-not (Test-Path $uninstallExe)) { throw 'DSH-Uninstall.exe 编译失败' }

# 卸载程序要**跟着安装器一起**落地:安装时会被铺到目标机器,
# 以后用户从"应用和功能"里点卸载,靠的就是它。
Copy-Item $uninstallExe (Join-Path $publishDir 'DSH-Uninstall.exe') -Force
Write-Host ("  Boot.exe {0:N0} KB / DSH-Uninstall.exe {1:N0} KB" -f `
    ((Get-Item $bootExe).Length / 1KB), ((Get-Item $uninstallExe).Length / 1KB))

Write-Host '[5/7] 打 payload.zip' -ForegroundColor Cyan
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $payloadZip -CompressionLevel Optimal
$zipMb = [math]::Round((Get-Item $payloadZip).Length / 1MB, 1)
Write-Host "  payload.zip $zipMb MB"

Write-Host '[6/7] 合成单个 exe(引导程序 + 附加 payload)' -ForegroundColor Cyan
$bootBytes = [System.IO.File]::ReadAllBytes($bootExe)
$zipBytes = [System.IO.File]::ReadAllBytes($payloadZip)
$stream = New-Object System.IO.FileStream($setupExe, [System.IO.FileMode]::Create)
try {
    $stream.Write($bootBytes, 0, $bootBytes.Length)
    $stream.Write($zipBytes, 0, $zipBytes.Length)
}
finally {
    $stream.Dispose()
}
$setupMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host "  $setupExe  ($setupMb MB)"

Write-Host '[7/7] 自检' -ForegroundColor Cyan
# 附加式自解压的命门:payload 必须正好接在 Boot.exe 后面,
# 偏移错了引导程序就找不到 zip(表现是"解压失败")。
if ([System.IO.File]::ReadAllBytes($setupExe)[$bootBytes.Length] -ne 0x50) {
    throw '合成结果不对:payload 的起点不是 zip 本地头(50 4B 03 04)'
}
$hash = (Get-FileHash $setupExe -Algorithm SHA256).Hash
Write-Host "  SHA256 $hash"
Write-Host ("  体积比自包含省了约 {0:N0} MB" -f (119.4 - $setupMb))

if (-not $NoDesktop) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $target = Join-Path $desktop 'DSH-Installer-Setup.exe'
    Copy-Item $setupExe $target -Force
    Write-Host ''
    Write-Host "已放到桌面:$target" -ForegroundColor Green
    Write-Host "(DSH-Uninstall.exe 只在 dist\ 里;安装之后它会被自动铺到目标机器上)" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '完成。' -ForegroundColor Green
