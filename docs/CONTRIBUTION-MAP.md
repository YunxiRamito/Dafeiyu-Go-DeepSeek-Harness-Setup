# 功能贡献地图

本地图按当前源码列贡献入口和验证范围。它描述现有边界，不表示所有历史设计或测试场景均已完成。仓库内保留三个运行层：WinUI 向导、无 UI 的安装核心、系统兼容启动/恢复程序。

## 找到对应板块

| 功能 | 主要源码入口 | 对应验证 |
| --- | --- | --- |
| 页面与交互 | `src/DshInstaller/Pages/`、`Controls/`、`Styles/`；主题在 `Theme.cs`、`PlatformStyle.cs`，文案在 `Localization.cs` | WinUI build；导入页用 `tools/ImportPage.Ui.Smoke.ps1`，迁移 UI 用 `tools/OfficialMigrationUiSmoke.ps1` |
| 导航、会话与文件选择 | `MainWindow.xaml.cs`、`InstallSession.cs`、`InstallerPlan.cs`、`FolderDialog.cs`、`PathAccess.cs` | `InstallFailureRegression`；相关页面 UI smoke |
| 环境与安装检测 | `src/DshInstaller.Shared/Detection/`、`DshLocator.cs`、`DshInstallation.cs` | `DshLocatorRegression`；离线安装识别用 `DshBundleRegression` |
| 下载、部署、修复 | `src/DshInstaller.Shared/Install/` 的 `DownloadEngine`、`SegmentedDownloader`、`ComponentReuse`、`PortableDirectoryDeployment`、`InstallRunner`、`BuiltInSteps` | `DownloadRepairRegression`、`InstallFailureRegression`、`DshBundleRegression` |
| 启动器选择与部署 | `Install/LauncherFeed.cs`、`LauncherSelection.cs` 与安装步骤 | `LauncherSelectionRegression`、`InstallFailureRegression` |
| 官方客户端导入 | `src/DshInstaller/Shared/ImportSource.cs`、`DshDataDiscoveryService.cs`、`DshDataImportService.cs` 与 `Pages/ImportPage.*` | `ImportSourceRegression`、`DshDataImportRegression`、`tools/ImportPage.Ui.Smoke.ps1`、`tools/verify-dsh-import-core.ps1` |
| 备份、导出与恢复 | `src/DshInstaller.Shared/Backup/` 与 `Pages/ExportPage.*` | `OfficialDshExportRegression`、`tools/OfficialMigrationUiSmoke.ps1`；按场景补归档与取消样本 |
| 独立迁移 EXE | `boot/OfficialDshRestore/` 的协议、迁移与窗口；共享库用 Compile Link 复用部分核心 | `OfficialDshExportRegression` 的文件系统、SFX 和实际 EXE 场景；该目录的 UI smoke/官方运行验证脚本 |
| 引导、提权与卸载 | `boot/Boot.cs`、`boot/Uninstall.cs`、`ElevationHelper.cs`、`Install/UninstallSteps.cs` | `InstallFailureRegression` 加隔离安装/卸载验证；涉及提权应写明实际权限环境 |
| 打包、资源与元信息 | `pack-release.ps1`、`Directory.Build.props`、两个 `.csproj`、`.github/workflows/` | `tools/PackResources.Regression.ps1`，最终 EXE/ZIP/逐文件哈希验收 |

回归项目统一位于 `tools/<suite>/`。`tools/DshDataImportRegression/OfficialMigrationOptionsRegression.csproj` 另外覆盖导入选项；应与导入核心一起运行。构建与隔离环境示例见 [贡献说明](../CONTRIBUTING.md)。

## 模块依赖

- `src/DshInstaller` 引用 `DshInstaller.Shared`，负责 UI 与本次操作编排。UI 不应直接复刻下载/部署/备份核心。
- `src/DshInstaller.Shared` 是普通类库，按 `Detection`、`Install`、`Backup` 分域。新增无 UI 逻辑应放入相应域，避免加入页面代码。
- `boot/` 使用系统 .NET Framework 编译，不能直接依赖 WinUI 或 .NET 8-only API。`OfficialDshRestore` 的协议与迁移代码同时被共享库链接，移动文件必须同步修改所有编译入口。
- 官方导入/发现服务与启动器保持同源逻辑，`verify-dsh-import-core.ps1` 按规范化换行比较两个仓库的文件。修改时在 PR 写出关联仓库验证。

## 目录改动的验证范围

XAML 路径对应编译后的 XBF 和资源路径；打包以源 XAML 清单校验资源。移动页面、控件或 `boot/OfficialDshRestore` 应单独提交，连同项目链接、资源 URI、测试引用和打包断言一起更新，重新验证发布资源。新增功能文件优先放入现有域；拆目录与改业务可分别审查。

安装器版本与产品属性来自 `Directory.Build.props`。普通贡献不需要调整发布版本、Release 资产、签名或用户数据目录。
