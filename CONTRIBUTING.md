# 参与贡献

先按功能板块定位入口，再提交范围明确的 PR。安装器、启动器、Connect 插件、Aries 和 Android 已是独立仓库；安装器仓库内按 UI、安装核心、兼容启动层组织，通常不需要新增仓库或程序集。

- 当前文件与验证入口见 [功能贡献地图](docs/CONTRIBUTION-MAP.md)。
- 产品行为先读 [README](README.md)，近期改动读 [交接说明](HANDOVER.md) 与 [变更记录](docs/CHANGELOG.md)。
- [ARCHITECTURE](docs/ARCHITECTURE.md) 包含早期设计和历史流程；涉及当前实现时，以源码、贡献地图和实际验证记录为准。

## 按功能提交 PR

一个 PR 聚焦一项用户行为或一个模块。说明复现条件、改后的行为、受影响路径及实际运行的验证。纯目录整理应单独提交，并在同一 PR 修正项目链接、资源路径、测试和打包清单；不要把大量移动混入问题修复。

修改官方导入、备份或恢复时，使用隔离样本，覆盖源数据保持不变、已有目标不覆盖、取消和路径边界。不要提交真实对话、凭据、个人路径或身份数据。修改 UI 时同时考虑明暗主题、中文/英文、出现/切换/关闭动画和原生文件夹选择。已有控件与系统图标优先。

## 本地构建与验证

需要 Windows x64、.NET 8 SDK、Windows SDK 和系统 .NET Framework 编译器。先将 SDK/NuGet/临时输出指向项目工作区或指定目录，不要依赖个人 C: 缓存。以下在本仓库根目录运行；工作区示例保留在 G:。

```powershell
$qa = [IO.Path]::GetFullPath('../Temp_SetupandLauncher/Contributions/my-change')
New-Item -ItemType Directory -Force -Path $qa | Out-Null
$env:TEMP = $qa
$env:TMP = $qa
$env:DOTNET_CLI_HOME = Join-Path $qa 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DAFEIYU_INSTALLER_SETTINGS_DIRECTORY = Join-Path $qa 'installer-settings'
$env:DSH_HOME = Join-Path $qa 'dsh-home'
$env:DSH_AGENTS_HOME = Join-Path $qa 'agents-home'
$env:NUGET_PACKAGES = 'G:/DeepSeek DSH/.nuget-packages'
$env:DAFEIYU_INSTALLER_TEMP_ROOT = [IO.Path]::GetFullPath('../Temp_SetupandLauncher')
$env:DAFEIYU_IMPORT_TEST_ROOT = $qa

dotnet build src/DshInstaller/DshInstaller.csproj -c Release --artifacts-path (Join-Path $qa 'build')
dotnet run --project tools/ImportSourceRegression/ImportSourceRegression.csproj -c Release --artifacts-path (Join-Path $qa 'import-source')
dotnet run --project tools/DshDataImportRegression/DshDataImportRegression.csproj -c Release --artifacts-path (Join-Path $qa 'import-core')
```

按贡献地图选择相关 suite。`OfficialDshExportRegression/run.ps1 -TaskDirectory <G:隔离目录>` 会实际生成并执行恢复 EXE；`DshLocatorRegression` 与 `LauncherSelectionRegression` 需要绝对沙箱目录参数。UI smoke 的 `Exe`、源/目标、输出目录必须是测试副本；跳页预览或 `--dry-run` 本身不等于隔离环境。记录命令、退出码和日志位置，无法运行的检查要明确写出。

## 跨仓边界与发布

`src/DshInstaller/Shared/DshDataImportService.cs` 和 `DshDataDiscoveryService.cs` 与启动器的 `source/Backup/` 副本保持一致；改动后运行 `tools/verify-dsh-import-core.ps1`，并说明关联仓库改动。Connect 协议或插件行为应在相应仓库验证。

普通 PR 无需改变版本号、签名配置或发布产物。打包改动额外运行资源回归并核对最终 EXE 内的 ZIP、XBF、PRI 与哈希。发布由维护者按当前 [发布流程](RELEASE.md)、[签名说明](SIGNING.md) 和工作流执行；本地构建、回归通过与正式发布分别记录。
