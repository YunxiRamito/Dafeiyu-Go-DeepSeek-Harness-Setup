# 交接：安装器 1.5.0 的 9 项改进

> 写给下一个接手的人。仓库：`Dafeiyu-Go-DeepSeek-Harness-Setup`
> **只改这个仓库**，不要把 `Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`（启动器）一起改。
> 上一轮做到一半被打断，下面「已完成」和「待办」都是核对过仓库现状写的（不是计划，是事实）。

最后更新：2026-09-27

---

## 一、现在的真实状态

`dotnet build src\DshInstaller\DshInstaller.csproj -c Release` 目前是**能过的**（0 错误）。`tools\Probe` 也过。

### 已完成（已在工作区，未提交）

| # | 事项 | 落在哪 |
|---|------|--------|
| 1 | 镜像池改成 `gh-proxy.com → ghproxy.net`，**删掉 `ghfast.top`** | `src\DshInstaller.Shared\Install\MirrorSource.cs` |
| 2 | 下载停滞看门狗：连续没新数据判定停滞、掐连接、**带 `Range` 续传**重连；另有「速度掉到阈值以下就换源」 | `src\DshInstaller.Shared\Install\DownloadEngine.cs`（`StallSeconds`、`SlowBytesPerSecond`）、`SegmentedDownloader.cs` |
| 3 | 进度行显示**实时速度 + 当前镜像**：`下载中 · 1.2 MB/s · 8.4 MB / 24 MB · 镜像名` | `DownloadEngine.cs`（`DownloadProgress.BytesPerSecond` / `SpeedText` / `SourceLabel`）、`BuiltInSteps.cs`（`DescribeDownload`）、`MirrorSource.cs`（`IsMirrorUrl` 等取「人话」镜像名） |
| 10 | 代理支持**开头**：`ProxySupport.cs`（新文件）+ 配置落盘 | `src\DshInstaller.Shared\ProxySupport.cs`、`src\DshInstaller.Shared\ConfigStore.cs` |
| 4 | 一键导出安装日志：失败页/完成页各一个按钮，打包 `installer*.log` / `launcher*.log` / `dsh-boot.log`，完事弹路径 + 「打开文件夹」。**逐行擦洗** token/api_key/Bearer/sk- 之类，名字带 token/secret/key/credential 的文件一律不收 | `src\DshInstaller.Shared\LogBundle.cs`（新）、`src\DshInstaller\LogExportUi.cs`（新）、`FailedPage` / `DonePage` |
| 5 | 装前体检：磁盘剩余、系统版本、已装 DSH（路径+版本）、8787 端口、系统代理；风险项浅黄 + 一句怎么办 | `src\DshInstaller.Shared\Detection\Preflight.cs`（新，复用 `EnvironmentProbe` / `ComponentProbe` 的结果）、`DetectPage` |
| 6 | 复用已有组件：本机 Node（≥22.13，走 probe 的版本判定）/ Git / pnpm / Python 够新就直接用，组件页写「已检测到，将直接使用」；`--force-reinstall` + 组件页「强制重新下载组件」开关。PATH/DSH 步骤都改成认复用路径 | `src\DshInstaller.Shared\Install\ComponentReuse.cs`（新）、`InstallOptions`、`BuiltInSteps`（Node/Dsh/Git/Pnpm/Python/Path）、`ComponentsPage`、`DevOptions` |
| 7 | 修复模式：`--repair` + 欢迎页「修复安装」按钮。开场核对 DSH 标志文件 / 启动器 / 卸载程序 / 卸载入口，缺的由后续**同一条幂等安装流程**补齐；不删数据、不重走向导 | `BuiltInSteps.RepairScan`（`IdRepair`）、`InstallSession.SetupRepair()`、`Program.cs`、`WelcomePage`、`InstallerPlan` |
| 8 | 卸载可保留数据：勾选框改成「保留我的设置、技能、插件与会话记录」（**默认保留**）。删 DSH 本体时走**选择性删除**，`<本体>\.dsh` 与 `<本体>\plugins` 原样留下；取消勾选才整目录清 | `UninstallOptions.KeepUserData`、`UninstallSteps`（`NormalizeKeeps` / `DeleteTreeKeeping`，都 public 以便单测）、`UninstallItemsPage` |
| 10 | 代理**收尾**：源选择页三选一（不使用 / 跟随系统 / 自定义 http·https·socks5 + 地址端口，落盘、记住）；`DownloadEngine` / `SegmentedDownloader` 的 HttpClient 按代际号**自动重建**；推荐插件列表、所有走 `ProcessRunner` 的子进程（npm/pnpm/git）都带上代理环境变量；回环一律绕过 | `SourcePage`、`ProxySupport`（`Generation` / `ApplyTo`）、`DownloadEngine`、`SegmentedDownloader`、`RecommendedPlugins`、`ProcessRunner`、`Program.cs` |

`git status` 里未提交的文件就是上面这些。

### 已验 / 未验

验过（都是真跑出来的，不是"看代码觉得对"）：

- 4 日志导出：真机日志打了一次，zip 里 5 个日志文件，敏感词扫描 0 命中（真实 `launcher.log` 9191 行）；擦洗自测通过。
  - 踩到一个坑记在这儿：`ZipArchive` 在 **Create 模式**下读 `Entries` 直接抛 `NotSupportedException`，被 catch 吞掉后表现成"每个文件都悄悄失败"，报错还被我写成了"文件被占用"。现在重名靠自己的 `HashSet` 记，失败原因原样带出来。
- 5 体检：磁盘 9.7 GB / 23H2 build 22635 / DSH 0.1.5-rc.3 / 8787 被 DSH 占用 —— 和系统实际值逐条核过；端口检测用 45999 空→占→空验过。
- 6 复用：本机 Node 26.7.0、Git 2.50.1、pnpm 12.4.1 都判为可复用，步骤报「已检测到，将直接使用」；开强制重装后回到下载分支。
- 7 修复：临时目录里造缺失，扫描报「发现 4 项需要修复」，补上文件后报 2 项。
- 8 卸载保留：在临时目录造了混合树（`.dsh\sessions`、`.dsh\skills`、`plugins`、`node_modules`、`launcher`、散文件、深层 `deep\nested\keep`），选择性删除后用户数据全在、程序文件全没；跑到 root 外面的路径和重复项都被丢掉。
- 10 代理：三种模式的环境变量、回环绕过、填不全时按直连、`cmd` 子进程真的收到 `HTTP_PROXY`、代际号会递增（HttpClient 重建）都验过。

**没验的（老实说）**：界面没法在这里点，所有 GUI 交互（按钮、单选、勾选框）只做到"编译过 + XAML 命名对得上"，没真人点过一次。上手第一件事建议按第四节把 4/5/6/7/8/10 的界面各点一遍。


---

## 二、实测数据（别再自己猜顺序）

虚拟机 `DESKTOP-G4QS2K7`（Win10，`192.168.188.130`），2026-09-27，三轮结论：

| 地址 | 结果 |
|------|------|
| `gh-proxy.com/<github 地址>` | ✅ 通，**唯一回 206 带 `Content-Range`** —— 只有它能多线程分段/断点续传 |
| `ghproxy.net/<github 地址>` | ✅ 通，但只回 200，不接受 `Range` |
| `ghfast.top/<github 地址>` | ❌ **每次都超时**（留着的唯一效果是让用户多等十几秒） |
| `codeload.github.com/<o>/<r>/tar.gz/<ref>` | ✅ 最快（0.65 s）但不接受 `Range` |
| `github.com/<o>/<r>/archive/<ref>.tar.gz` 直连 | ❌ 超时 |
| `cdn.jsdelivr.net/gh/...`（raw 文件） | ❌ 超时 |
| `raw.githubusercontent.com` 直连 | ❌ 超时 |

另一条更早的实测（同上机器）：一条候选「连得上、响应头秒回、正文不吐数据」时，旧代码能卡 **6 分 18 秒**——这就是第 2 项要修的东西。

---

## 三、硬性约定

1. **文案**：砍半再砍半、信息前置、按钮用动词、错误文案 = 「发生了什么 + 怎么办」。不要专业术语、不要出现文件路径当解释。
2. **所有界面字符串中英成对进 `Localization.cs`**，别写死在 XAML 里。
3. **不要手改版本号**：版本由父级 `set-version.ps1`（改 `Directory.Build.props` 的 `InstallerVersion` + 启动器那几处）统一改。
4. **改完更新 `docs\CHANGELOG.md`**，顶部加一节 `## [1.5.0] - <日期>`，大白话、每条一两句、不写内部实现和文件名。中英各一份。
5. **每做完一项就编译一次**，别攒着。
6. 国内 / 官方线路**严格分流**：加速线路才用 npmmirror、华为云、GitHub 镜像；官方线路不套第三方镜像。
7. 回环地址（`127.0.0.1`、`localhost`）**永远绕过代理**（跟启动器 `ProxySupport.cs` 的语义一致，可只读参考那个文件）。
8. **XAML 里的默认选中别写死**：`ComboBox` / `RadioButtons` 的 `SelectedIndex="0"` 在首次布局时会触发一次 `SelectionChanged`，会把刚读出来的设置覆盖掉（启动器上被这个坑坑过一次：代理设置重启后变回「不使用代理」）。要么去掉写死的索引，要么加「控件稳定前不落盘」的闸门。
9. **构建可能需要完整文件权限**：WinUI 相关的生成步骤在受限沙箱里会报 `Win32Exception (5) 拒绝访问`，那时要用完整权限跑，或改成直接 `dotnet build` 那个 csproj。

**本机可用的 dotnet**：`G:\DeepSeek DSH\.tools\dotnet\dotnet.exe`

```
G:\DeepSeek DSH\.tools\dotnet\dotnet.exe build `
  "G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup\src\DshInstaller\DshInstaller.csproj" `
  -c Release --nologo
```

---

## 四、每项怎么验（别只写代码不验）

- **2 停滞看门狗**：临时把 `StallSeconds` 调小（比如 3 秒），用一个会挂起的 URL（或断网后恢复）跑一次，日志里要看到「判定停滞 → 换源/续传」，且**已下载的字节没有从头再来**。
- **3 速度显示**：跑一次真实下载，进度行三要素齐全（速度非 0、已下/总量合理、镜像名是人话），且速度不要每秒乱跳（平滑窗口 ≥0.5 秒）。
- **4 日志导出**：真点一次按钮，确认 zip **真的产出**、路径可打开、里面只有日志文件。
- **5 体检**：故意占住 `8787`（起个监听），确认报告能标出来；再确认磁盘/版本数值和系统实际一致。
- **6 复用组件**：在有 Node/Git/pnpm 的机器上跑，确认那几项被标成「将直接使用」且真的没下载；打开「强制重装」后恢复下载。
- **7 修复**：手工删掉一个已装文件，跑 `--repair`，确认只补回那个文件、用户数据没动。
- **8 卸载保留**：选「保留」卸载后确认设置/技能/插件还在；选「彻底清除」后确认都清干净。
- **10 代理**：填一个能通的代理确认下载走代理；把代理填成错的确认报错文案是「发生了什么 + 怎么办」；确认回环地址没被代理。

---

## 五、已知坑 / 别踩

- 上一轮改动的**注释里带了实测数据**，别当废话删掉——那是下次改顺序时的唯一依据。
- `MirrorSource.cs` 里新加的 `IsMirrorUrl` 一类 helper 是给进度文案用的，改文案时先看它。
- 启动器仓库有自己那份 `HANDOVER.md` 和 1.5.0 更新日志，**那边不需要你动**（安装器与启动器不共享下载/加速代码，只有约定保持一致）。
- `ZipArchive` 在 **Create 模式**下不能读 `Entries`（直接抛 `NotSupportedException`）。要防重名就自己拿 `HashSet` 记，别去问 archive。
- 代理改了之后 **HttpClient 必须重建**才生效。这里的做法是 `ProxySupport.Generation` 代际号 + 取用时比一比，别改回 `static readonly`。
- 卸载的**选择性删除**（`DeleteTreeKeeping`）收尾时**不能删 root 自己** —— 保留项就挂在它底下，顺手一发递归删除会把用户数据删干净。这条是这套逻辑里最容易写错的地方。
- `ProcessStartInfo.EnvironmentVariables` 是 `StringDictionary`（不区分大小写），但它不是 `IDictionary<string,string>`。往里写代理变量前要先按不区分大小写把旧键删掉，否则环境里同时挂两份。

