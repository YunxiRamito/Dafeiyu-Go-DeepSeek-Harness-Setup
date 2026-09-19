# 开发与构建

## 环境

| 东西 | 位置 |
|------|------|
| .NET SDK | `G:\DeepSeek DSH\.tools\dotnet\dotnet.exe` |
| NuGet 缓存 | `G:\DeepSeek DSH\.nuget-packages` |
| .NET Framework 编译器 | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| Windows App SDK | `Microsoft.WindowsAppSDK 1.8.260804001`(缓存里已有) |

**每次构建前先设缓存目录**,不然会去默认的 `%USERPROFILE%\.nuget` 重新下:

```powershell
$env:NUGET_PACKAGES = 'G:\DeepSeek DSH\.nuget-packages'
```

## 常用命令

```powershell
# 只编共享库(改核心逻辑时最快)
cd 'G:\DeepSeek DSH\DSH Works\DSH Installer'
$env:NUGET_PACKAGES = 'G:\DeepSeek DSH\.nuget-packages'
& 'G:\DeepSeek DSH\.tools\dotnet\dotnet.exe' build .\src\DshInstaller.Shared\DshInstaller.Shared.csproj -c Release

# 全量构建(UI + 引导 + 卸载器 + 打包启动器),不合成 Setup.exe
.\build.ps1
.\build.ps1 -SkipLauncher          # 还没编启动器时

# 正式打包:框架依赖发布 → 补 XAML 资源 → 编引导/卸载器 → 合成单个 Setup.exe → 拷桌面
.\pack-release.ps1
.\pack-release.ps1 -NoDesktop      # 只在 dist\ 里产出

# 换图标(改了配色/图形之后重新生成 assets\DSHInstaller.ico)
.\tools\make-icon.ps1
```

## 构建产物

```
dist\
├─ DSH-Installer-Setup.exe    发给用户的单文件安装包(引导 + 附加 payload,约 10.5 MB)
├─ Boot.exe                   引导程序(.NET Framework,csc 直编)
├─ DSH-Uninstall.exe          独立卸载程序(会被安装器铺到目标机器)
├─ payload.zip                安装器本体那一段(附加在 Setup.exe 末尾)
├─ selfcontained\             发布出来的安装器目录(名字是历史遗留,现在是框架依赖)
└─ preview\                   开发预览用的发布输出(pack-preview.ps1)
```

## 调试

| 场景 | 怎么做 |
|------|--------|
| 不想真装 | 主程序带 `--dry-run`,只走流程不落盘 |
| 看日志 | `%LOCALAPPDATA%\DeepSeekHarness\installer.log` |
| 引导阶段日志 | `%TEMP%\dsh-boot.log`(运行库检测/下载/解压都记在这儿) |
| 卸载器日志 | `%TEMP%\dsh-uninstall.log` |
| 状态文件 | `%LOCALAPPDATA%\DeepSeekHarness\installer-state.json` |
| 想重置 | 删掉 `%LOCALAPPDATA%\DeepSeekHarness` 整个目录 |
| 提权 worker 单独调 | `DSH-Installer.exe --dsh-installer-worker <参数>` |
| 只装运行库 | `dist\Boot.exe --runtimes-only`(提权实例走的就是这条路) |

## 打包启动器 payload

启动器源码在另一个项目(版本号见 `build.ps1` 里的 `$launcherOut`):

```powershell
cd 'G:\DeepSeek DSH\DSH Works\Project\DeepSeek Starter\source'
$env:NUGET_PACKAGES = 'G:\DeepSeek DSH\.nuget-packages'
.\build-winui.ps1 -OutputDirectory (Join-Path $PWD 'dist-1.3.21')
# 再手动编外层引导
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x64 /optimize+ `
  "/win32icon:$PWD\DeepSeekHarness.ico" "/win32manifest:$PWD\RuntimeBootstrap.manifest" `
  "/out:$PWD\dist-1.3.21\DeepSeek Harness.exe" "$PWD\RuntimeBootstrap.cs"
```

然后回本目录跑 `.\build.ps1`(它会去那个目录拿文件打 zip)。

> 注意:安装器**不把启动器打包进安装包**,只在断网时回落到这份 payload。
> 正常路径是读启动器仓库的 `manifest.json` 现下最新版 —— 所以启动器发新版不需要重发安装器。

## 注意

- **所有 `.ps1` 存成 UTF-8 with BOM**。Windows PowerShell 5.1 读无 BOM 的 UTF-8 会把中文变成乱码并直接语法报错。
- `build.ps1`、`Directory.Build.props` 里的版本号要跟 `WellKnown.cs` 对齐。
- `payload\` 和 `dist\` 不进 git(见 `.gitignore`)。
