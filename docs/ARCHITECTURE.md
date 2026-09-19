# 架构与页面流程

## 技术选型

| 部分 | 选型 | 理由 |
|------|------|------|
| 主程序 | WinUI 3 未打包 (`WindowsPackageType=None`) | 现代外观;未打包才能自由提权、自由读写任意目录 |
| 目标框架 | `net8.0-windows10.0.19041.0` | 跟启动器一致 |
| 运行时 | 框架依赖 (`SelfContained=false`) | 体积小 |
| 引导程序 | .NET Framework 4.x + WinForms,csc 直编 | 任何 Win10 都自带,不依赖任何东西 |
| 共享库 | `net8.0-windows` 普通类库 | UI 和卸载器共用 |
| 语言 | C# 无 nullable、无 ImplicitUsings,显式 delegate | 跟启动器一致,少踩坑 |

### 为什么需要 Boot.exe(真引导,不只是自解压)

WinUI 3 未打包应用启动时必须先跑 `Microsoft.WindowsAppRuntime.Bootstrap`,而 Bootstrap 要求
系统已安装 **Windows App Runtime 1.8**;主程序本身是框架依赖的,还需要 **.NET 8 桌面运行时**。
新机器上这两个多半都没有 —— 于是有了引导程序:

```
DSH-Installer-Setup.exe(= Boot.exe + 附加的 payload.zip)
  1. 检查两个运行库(.NET 8 桌面运行时 / Windows App Runtime 1.8)
       都有 → 直接解压 + 启动主程序(全程不提权,一次 UAC 都不弹)
       缺   → 自己没提权就先 runas 起一个 --runtimes-only 的副本
              → 下载 + 静默安装(装的时候弹一个极简进度窗)
              → 装完回到原来的实例继续
       装不上 → 给一次性提示,提供官方下载页,别让用户对着闪退干瞪眼
  2. 解压附加在自己末尾的 payload 到 %LOCALAPPDATA%\DeepSeekHarness\Boot
  3. 启动里面的 DSH-Installer.exe,命令行原样传下去(--silent 之类)
  4. 等主程序退出。**刻意不清理解压目录** —— 见下面"卸载入口"
```

**为什么运行库不打包进去**:一起打进去安装包就是 119 MB,每个用户都得先下完;
而现在 Setup.exe 只有 10.5 MB,缺什么下什么。这一步的代价是"装的时候要联网",
换来的是十几倍小的安装包。

**卸载入口**:安装器跑在引导程序解压出来的目录里,如果装完就清理掉,用户回头想卸载时
机器上**根本没有安装器可跑**(实测踩过)。所以:
- 解压目录保留;
- 安装时把 `DSH-Uninstall.exe` 铺到启动器目录,并注册到"应用和功能";
- 那个独立卸载程序会先把自己连同安装器复制到 `%TEMP%` 再跑 ——
  直接原地跑的话,卸载流程删不掉自己所在的目录(文件被运行中的进程锁着)。

主程序自己是 `asInvoker`(不提权),需要管理员时用 `ElevationHelper.StartElevatedWorker()`
把自己以 `runas` 再拉一次,带 `--dsh-installer-worker`,只弹一次 UAC。
缺运行库时 `InstallSession.NeedsElevation` 也会返回 true —— 装运行库是机器级操作。

---

## 页面流程

```
┌─────────────────────────────────────────────────────────────┐
│ 0 欢迎     流光动画 + 中英双语标题 + 语言切换                 │
│            build < 17763 → 直接在这里劝退(不下下一步)        │
├─────────────────────────────────────────────────────────────┤
│ 1 安装范围   ○ 仅本用户  (%LOCALAPPDATA%\DeepSeek Harness)   │
│             ○ 所有用户  (%ProgramFiles%\DeepSeek Harness)   │
│             (选机器范围时,确认页会触发一次 UAC)              │
├─────────────────────────────────────────────────────────────┤
│ 2 环境检测  逐项:Windows / Node / npm / DSH / WinAppRuntime  │
│            / .NET8 / pnpm / Git / Python                     │
│            全绿 → 直接跳第 4 页                              │
│            有缺 → 第 3 页                                    │
├─────────────────────────────────────────────────────────────┤
│ 3 组件安装   [组件根目录 ▾ 浏览]  [下载源: 官方/镜像]        │
│            每行:名称 + 状态 + 环形进度 + 速度 + 取消         │
│            便携安装,不写系统(除 Node)                      │
├─────────────────────────────────────────────────────────────┤
│ 4 DSH 位置  已检测到 → 显示路径,可直接用                     │
│            没有 → 选目录,这里会 npm install                 │
├─────────────────────────────────────────────────────────────┤
│ 5 启动器位置 默认 <DSH 根>\DeepSeek Harness,可改             │
├─────────────────────────────────────────────────────────────┤
│ 6 确认     把所有选择列一张清单,一个"开始安装"               │
├─────────────────────────────────────────────────────────────┤
│ 7 进度     逐个任务:下载(环+速度) → 解包(Win11 加载圈)     │
│            → 配置(打勾)。失败可重试,可复制日志             │
├─────────────────────────────────────────────────────────────┤
│ 8 完成     ☑立即启动 ☑开机自启 ☑桌面快捷方式                 │
└─────────────────────────────────────────────────────────────┘
```

**动画规范**
- 页面切换:内容 `Offset.X 24 → 0` + 透明度,180ms,`CubicEase`
- 下载态:环形进度(`ProgressRing` + 自定义中心文字)
- 解包/安装态:Win11 六点加载圈
- 完成态:对勾缩放出现(0.85 → 1.0)

---

## 组件安装矩阵

| 组件 | 必需 | 装到哪 | 来源 | 解包后要做的事 |
|------|------|--------|------|----------------|
| Windows App Runtime 1.8 | 是 | 系统(msix) | aka.ms | 静默安装 |
| .NET 8 桌面运行时 | 是 | 系统(msi) | dotnet.microsoft.com | 静默安装 |
| Node.js 22 LTS | 是 | 系统(全局) | 官方 / npmmirror | 解压 → 写 PATH → 校验 `node -v` |
| npm | 是 | 随 Node | — | 不用管 |
| DSH 本体 | 是 | 用户选 | npm registry | `npm install @deepseek-ai/dsh@^0.1.5-rc.1 node@^24` |
| pnpm | 可选 | 便携 | GitHub releases | 解压成 `pnpm.exe` |
| Git (MinGit) | 可选 | 便携 | GitHub releases | 解压 |
| Python (embeddable) | 可选 | 便携 | python.org | 解压 + 写 `python312._pth` 开 site-packages |
| 启动器 | 是 | 用户选 | 本安装器 payload | 解压 `launcher.zip` |

**Node 的处理细节**
- 检测到 `>= 22.13.0` → 一个字都不动
- 检测到但太旧 → 提示,默认装 22 LTS(用户可跳过)
- 没装 → 下载 `node-v22.x.x-win-x64.7z`(比 zip 小),解到 `C:\Program Files\nodejs`
- 写 PATH:先查重,再检查长度(< 1800),注册表广播 `WM_SETTINGCHANGE`

**镜像策略**
- 默认按系统语言/时区猜一次,界面可切
- 官方与镜像排成数组,`DownloadEngine` 挨个试,失败自动换
- npm 走 `--registry=<源>`

---

## 核心类职责

### Shared

| 类 | 职责 | 关键点 |
|----|------|--------|
| `WellKnown` | 常量:包名、版本、路径、参数 | 改门槛只改这里和 `Directory.Build.props` |
| `EnvironmentProbe` | 整机体检 | PATH 要拼 HKLM+HKCU;系统版本走 `RtlGetVersion` |
| `ComponentStatus` / `ProbeReport` | 检测结果模型 | `Required` + `IsSatisfied` 决定能否下一步 |
| `DshLocator` | 找 DSH 根目录 | 顺序:运行中进程 → Run 键 → 状态文件 → 常见位置 → 扫一层盘 |
| `DownloadEngine` | 下载 | 多源回退 / `Range` 续传 / 0.25s 报一次进度 |
| `MirrorSource` | 地址表 | 官方与镜像配对,`Order()` 按偏好排序 |
| `ArchiveExtractor` | 解包 | zip 用 .NET;7z 先找 `7z.exe`,再退 `tar.exe` |
| `LauncherFeed` | 启动器发布清单 | 读仓库 manifest → 版本/地址/sha256,自动配镜像前缀 |
| `ProcessRunner` | 跑命令 | 统一超时、编码、输出回调 |
| `PowerShellScript` | 跑 PS | 脚本从 stdin 喂,避开转义地狱 |
| `ShellLink` | 读写 .lnk | 走 `WScript.Shell` COM,不引互操作包 |
| `ElevationHelper` | 提权 | `runas` 拉自己 + `--dsh-installer-worker` |
| `ConfigStore` / `InstallerState` | 状态持久化 | `%LOCALAPPDATA%\DeepSeekHarness\installer-state.json` |
| `InstallLogger` | 日志 | 同目录 `installer.log`,排障就靠它 |

### UI(待写)

```
App.xaml(.cs)                 应用入口
MainWindow.xaml(.cs)          自绘标题栏 + Frame + 底部按钮条
InstallerSession.cs           单例:本次安装的所有选择 + 检测报告
Pages\
  WelcomePage / ScopePage / DetectPage / ComponentsPage
  DshLocationPage / LauncherLocationPage / ConfirmPage
  ProgressPage / DonePage
Controls\
  StepIndicator.xaml          顶部步骤条
  TaskRow.xaml                组件行:名称 + 环形进度 + 速度 + 状态
  FolderPicker.xaml           路径框 + 浏览按钮(用 Win32 文件夹选择器)
  RingProgress.xaml           环形进度
Localization\
  Strings.cs / zh-CN.json / en-US.json
```

---

## 启动器分发(不打包,现下最新版)

安装器不把启动器塞进自己肚子里,而是每次去启动器仓库要最新版:

```
LauncherFeed.Fetch("owner/deepseek-harness-launcher", 源偏好)
  ├─ 国内源优先: https://cdn.jsdelivr.net/gh/<repo>@main/manifest.json
  ├─ 官方兜底:   https://raw.githubusercontent.com/<repo>/main/manifest.json
  ├─ assets.github → zip 地址,国内源时加 ghproxy.net / ghfast.top 前缀
  ├─ assets.mirrors[] → 自建镜像,挨个试
  └─ sha256 → 下完校验,对不上重下
       ↓ 拉不到清单
  payload\launcher.zip(安装器自带,离线兜底,允许是旧版本)
```

好处:**启动器发新版只需推启动器仓库,安装器一个字不用改**,
新装用户自动拿到最新启动器;顺带把安装器体积省掉约 37MB。

`WellKnown.LauncherRepository` 是仓库地址开关,留空则只用 payload。

发版流程与 manifest 字段说明写在启动器仓库的 `RELEASE.md` 里。

---

## 部署产物长什么样

用户机器上装完之后:

```
<用户选的 DSH 根>\
└─ DeepSeek Harness\                启动器
   ├─ DeepSeek Harness.exe          引导(requireAdministrator)
   ├─ DeepSeek Harness.Core.exe     WinUI3 主程序
   └─ ...dll

<组件根>(如果装了便携组件)\
├─ node\        (全局装时不在这么,在 Program Files)
├─ pnpm\pnpm.exe
├─ git\
└─ python\

注册表
  HKCU\...\Run\DeepSeek Harness              = "<启动文件夹>\DeepSeek Harness.lnk"
  HKCU\...\Uninstall\DeepSeekHarness         卸载信息(显示名/图标/卸载命令)
  HKCU\...\StartupApproved\StartupFolder     标记为"启用"

快捷方式
  桌面\DeepSeek Harness.lnk
  启动\DeepSeek Harness.lnk                 带 --no-browser
```

---

## 卸载流程

```
DSH-Installer-Uninstall.exe
  ├─ 问:确定卸载 DeepSeek Harness 启动器吗?
  ├─ 问:要不要一起卸载 Node.js?(只有本安装器装的 Node 才问)
  ├─ 关掉启动器进程(不动 DSH 服务)
  ├─ 删自启、快捷方式、注册表项
  ├─ 删启动器目录
  ├─ 删 DSH 本体  ← 单独问一次,默认不删(用户数据在里面)
  └─ 保留 %LOCALAPPDATA%\DeepSeekHarness\installer.log
```
