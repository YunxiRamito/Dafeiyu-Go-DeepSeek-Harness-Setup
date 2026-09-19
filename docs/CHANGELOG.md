# 变更记录

## [未发布] 1.0.0 — 开发中

### 打包与体积(2026-09-19)

- **运行库改成必选组件,在线按需安装**。`.NET 8 桌面运行时` 与 `Windows App Runtime 1.8`
  以前被一起打进安装包(自包含发布,119.4 MB);现在由引导程序在装的时候检测、缺了才下载 + 静默安装。
  **安装包从 119.4 MB 降到 10.5 MB**。
  - `boot\Boot.cs` 从"自解压壳"升级成**真引导**:探测运行库 → 缺则 `runas` 起一个
    `--runtimes-only` 副本下载安装(带进度窗)→ 解压 payload → 拉起安装器。
  - 装不上运行库时给明确提示 + 官方下载页,不再让用户面对"双击没反应"。
- **新增独立卸载程序 `DSH-Uninstall.exe`**(`boot\Uninstall.cs`):
  - 安装时被铺到启动器目录,并注册到"应用和功能"(`UninstallString` / `QuietUninstallString`);
  - 运行时会把自己和安装器复制到 `%TEMP%` 再执行 —— 否则卸载流程删不掉自己所在的目录;
  - 机器级安装自动申请一次提权,并支持 `--silent` 无人值守卸载。
- **安装器自我保留**:引导程序解压出来的 `%LOCALAPPDATA%\DeepSeekHarness\Boot` 装完不再清理,
  否则用户回头想卸载时机器上根本没有安装器可跑(实测踩过)。新增步骤 `uninstaller`(部署卸载程序)
  和对应的卸载步骤 `registry`(移除卸载入口、清理副本)。
- **图标**:`assets\DSHInstaller.ico`,由 `tools\make-icon.ps1` 生成(圆角蓝底 + 白色鲸鱼,7 个尺寸)。
- **双语 README**:`README.md`,中英逐节对照。
- `pack-release.ps1` 重写:框架依赖发布 → 补 `.xbf`/`.pri` → 编 `Boot.exe` 与 `DSH-Uninstall.exe`
  → 打 payload → 二进制拼接 → 自检(校验 payload 起始字节 + 打印 SHA256)→ 拷桌面。

### 界面

- 组件页改成**必选在上、可选在下**的单列布局(原来左右并排,顺序看不出来),整块可滚动。
- 安装计划顺序调整为"必选在前、可选在后":运行库 → Node → DSH 本体 → 启动器 → 卸载入口
  → 快捷方式 / PATH / 自启 → (可选:Git / pnpm / Python)→ 收尾校验。
- 缺运行库时也会触发一次提权(`InstallSession.NeedsElevation`),不然"仅为本用户"的用户
  会在进度页撞上看不懂的安装失败。

### 新增

- 项目骨架:`Directory.Build.props` / `build.ps1` / `docs\`
- 共享库 `DshInstaller.Shared`:
  - `WellKnown` 常量表(包名、版本、系统门槛、注册表路径、参数)
  - `EnvironmentProbe` 整机体检:Windows / Node / npm / pnpm / Git / Python / DSH / WinAppRuntime / .NET8
  - `RuntimeProbe` 两个必装运行库的存在性判断(启动早期就要用,所以单独拎出来)
  - `DshLocator` 定位 DSH 根目录(运行进程 → Run 键 → 状态文件 → 常见位置 → 扫盘)
  - `DownloadEngine` 多源回退 + Range 断点续传 + 实时测速
  - `MirrorSource` 官方/镜像地址配对与排序,Node LTS 与 .NET 运行时版本解析
  - `ArchiveExtractor` zip / 7z / tar.gz 解包(优先系统自带能力)
  - `ProcessRunner` 统一的进程调用(超时、UTF-8、输出回调)
  - `PowerShellScript` 从 stdin 喂脚本执行 PowerShell
  - `ShellLink` 读写快捷方式(COM,无额外依赖)
  - `ElevationHelper` 管理员判断与 `runas` 自提权
  - `ConfigStore` / `InstallerState` / `InstallLogger` 状态与日志
- 组件安装编排:`BuiltInSteps`(运行库 / Node / DSH / 启动器 / 卸载入口 / 快捷方式 / PATH / 自启 / 校验)
  + `InstallRunner`(顺序执行,失败不中止,补救重试轮)
- 卸载逻辑 `UninstallSteps`,与安装共用同一套执行器
- WinUI3 向导:自绘标题栏 + 步骤条 + 10 个页面,中英双语
- 引导程序 `Boot.exe`、独立卸载程序 `DSH-Uninstall.exe`
- 文档:`STATUS.md`(交接主文档)、`README.md`(用户向双语)、
  `docs\ARCHITECTURE.md`、`docs\SETUP.md`、`docs\文案清单.md`

### 已确认的外部事实

- `@deepseek-ai/dsh` 已发布到 npm(`0.1.5-rc.1`)
- Node 22 线最新 LTS:`v22.23.2`
- 最低系统 Windows 10 1809 (build 17763)
- 启动器仓库:`YunxiRamito/DSH-Launcher`,清单 `manifest.json` 现下最新版

### 待办

- 干净虚拟机上端到端验证本轮改造(运行库按需安装、独立卸载器、必选/可选排序)。
- 图标在真实任务栏/开始菜单里的观感复核。
- 代码签名(暂不签,README 里写清楚了 SmartScreen 怎么过)。
- GitHub 仓库上传(**等用户指令**)。
