# 发布安装器:build -> publish -> 补 .xbf -> 覆盖 dist\preview
# 注意:publish 不会带 .xbf(编译后的 XAML),必须从 bin 补过去,
# 否则界面改动完全不生效 —— 只拷 exe/dll 而漏掉 .xbf/.pri 就是这样踩的。
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = $PSScriptRoot
$dotnet = 'G:\DeepSeek DSH\.tools\dotnet\dotnet.exe'
$env:NUGET_PACKAGES = 'G:\DeepSeek DSH\.nuget-packages'

$proj = Join-Path $root 'src\DshInstaller\DshInstaller.csproj'
$bin = Join-Path $root 'src\DshInstaller\bin\Release\net8.0-windows10.0.19041.0'
$out = Join-Path $root 'dist\preview'
$zip = Join-Path $root 'dist\DSH-Installer-Preview.zip'

Get-Process | Where-Object { $_.ProcessName -like '*DSH-Installer*' } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host '[1/4] 编译' -ForegroundColor Cyan
& $dotnet build $proj -c Release --nologo -v minimal 2>&1 |
    Select-String -Pattern ': error|已成功' | Select-Object -First 8 | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

Write-Host '[2/4] publish' -ForegroundColor Cyan

# 先确认输出目录真的清空了。
# 之前这里被运行中的进程锁住,Remove-Item 静默失败,于是旧 .xbf 留在原地、
# 新 .xbf 跟它们并存 —— 界面改动看起来"完全没生效"就是因为加载到了旧的那份。
for ($attempt = 1; $attempt -le 5; $attempt++) {
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    if (-not (Test-Path $out)) { break }
    Write-Host "  第 $attempt 次清理没干净,重试" -ForegroundColor Yellow
}

if (Test-Path $out) {
    throw "输出目录清不掉,可能有进程占着:$out"
}
& $dotnet publish $proj -c Release -r win-x64 --self-contained false -o $out --nologo -v minimal 2>&1 |
    Select-String -Pattern ': error' | ForEach-Object { $_.Line }

Write-Host '[3/4] 补 .xbf(关键步骤)' -ForegroundColor Cyan
$binFull = (Resolve-Path -LiteralPath $bin).Path
$outFull = (Resolve-Path -LiteralPath $out).Path

$copied = 0
Get-ChildItem -LiteralPath $binFull -Recurse -File |
    Where-Object { $_.Extension -eq '.xbf' -or $_.Name -like '*.pri' } |
    ForEach-Object {
        $rel = [System.IO.Path]::GetRelativePath($binFull, $_.FullName)
        $target = Join-Path $outFull $rel
        $dir = Split-Path -Parent $target
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        $copied++
    }

Write-Host "  补了 $copied 个 .xbf/.pri" -ForegroundColor Green

# 清掉发布可能带出来的 RID 子目录(会和根目录的资源重复,容易加载到旧的那份)
$nested = Join-Path $out 'win-x64'
if (Test-Path $nested) {
    Remove-Item $nested -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '  已清掉多余的 win-x64 子目录' -ForegroundColor Green
}

# 核对:缺了就等于界面改动不会生效
$xbfCount = (Get-ChildItem $out -Recurse -Filter '*.xbf' | Measure-Object).Count
$priCount = (Get-ChildItem $out -Filter '*.pri' | Measure-Object).Count
if ($xbfCount -lt 10 -or $priCount -lt 1) {
    throw "产物不完整:.xbf=$xbfCount .pri=$priCount(界面不会更新)"
}
Write-Host "  核对通过:.xbf=$xbfCount  .pri=$priCount" -ForegroundColor Green

Write-Host '[4/4] 打包' -ForegroundColor Cyan
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("  {0}  {1:N1} MB  {2} 个文件" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB), (Get-ChildItem $out -Recurse -File | Measure-Object).Count) -ForegroundColor Green
