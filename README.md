# 大肥鱼Go / Dafeiyu-Go Setup

**大肥鱼Go，面向 DeepSeek Harness (DSH) 的一体化 Windows 安装程序。**
**Dafeiyu-Go, an all-in-one Windows setup for DeepSeek Harness (DSH).**

仓库名 `Dafeiyu-Go-DeepSeek-Harness-Setup` · MIT 协议 / MIT License

> **1.4.9 过渡版**：显示名称与界面先切换为“大肥鱼Go / Dafeiyu-Go”，
> 安装包、卸载注册表键和数据目录保持兼容。详见 [`TRANSITION.md`](TRANSITION.md)。

---

## 这是什么 / What it is

Dafeiyu-Go Setup 将「在一台干净的 Windows 机器上部署可用的 DSH」这一过程简化为一次双击。它负责检测环境、补齐缺失的依赖、安装 DSH 本体、部署启动器托盘程序、创建快捷方式，并提供独立的卸载程序。

Dafeiyu-Go Setup reduces the task of getting DSH running on a clean Windows machine to a single double-click. It detects the environment, fills in missing prerequisites, installs DSH itself, lays down the launcher tray app, creates shortcuts, and ships a standalone uninstaller.

一个安装器，而非一系列手工步骤：

One installer instead of a series of manual steps:

> 检测环境 → 补齐缺失项 → 安装 DSH 本体 → 部署启动器 → 创建快捷方式 → 可卸载
>
> Detect → fill the gaps → install DSH → install the launcher → create shortcuts → uninstallable

技术构成：向导界面用 **C# / .NET 8 / WinUI 3**（未打包应用形式），最外层引导程序用 **.NET Framework 4.x + WinForms** 编写。工程根目录就是 `DSH Installer\`。

Under the hood: the wizard is a **C# / .NET 8 / WinUI 3** unpackaged app, and the outermost bootstrapper is written in **.NET Framework 4.x + WinForms**. The project root is the `DSH Installer\` directory itself.

---

## 安装策略：全便携 / Install strategy: fully portable

除了 Node 和两个运行库，其他可选组件（pnpm / Git / Python）都是解压到用户自选目录，不写入系统、不改动 PATH 之外的内容，卸载即删除目录。

Apart from Node and two runtimes, every optional component (pnpm / Git / Python) is simply unpacked into a directory you choose. Nothing is written into the system, and uninstalling means deleting that directory.

**唯一会动系统的只有两样：Node 和两个运行库。**

**Only two things touch the system: Node, and the two runtimes.**

---

## 系统要求 / Requirements

| 项目 / Item | 要求 / Requirement |
| --- | --- |
| 操作系统 / OS | Windows 10 1809（build 17763）及以上 / Windows 10 1809 (build 17763) or newer |
| 架构 / Architecture | x64 |
| 权限 / Privileges | 普通用户即可开始；选择「所有用户」或「开机自启」时提权 / Standard user to start; elevation only for "all users" or autostart |
| 网络 / Network | 需要联网下载运行库与 DSH 本体 / Network access to fetch runtimes and DSH |

低于 build 17763 的系统**直接不支持**。安装器会停在欢迎页给出明确提示，不做兜底尝试——这是 Windows App Runtime 1.8 的最低版本要求决定的。

Anything below build 17763 is **not supported at all**. The installer stops on the welcome page with a clear message rather than attempting a fallback, because Windows App Runtime 1.8 sets that floor.

### 必装运行库 / Required runtimes

- **.NET 8 桌面运行时 / .NET 8 Desktop Runtime**
- **Windows App Runtime 1.8**

这两个库**不打进安装包**，而是在安装时按需**在线下载并静默安装**。

Neither runtime is bundled. The installer downloads and silently installs them on demand.

---

## 快速开始 / Quick start

1. 从 Releases 下载 `DSH-Installer-Setup.exe`。
2. 双击运行（首次会看到 SmartScreen 提示，处理办法见下文）。
3. 选择安装范围：仅当前用户 / 所有用户，以及可选组件、快捷方式、开机自启。
4. 点「安装」，只在需要时弹一次 UAC。
5. 安装完成后可直接启动 DSH。

1. Download `DSH-Installer-Setup.exe` from Releases.
2. Double-click it (the first run triggers a SmartScreen prompt — see below).
3. Pick the install scope (current user / all users), optional components, shortcuts, autostart.
4. Hit **Install**; a single UAC prompt appears only if needed.
5. DSH launches right away.

中间发生的事：

What happens in between:

- 检测 Node、pnpm、Git、Python 和两个运行库是否就位；
- 补齐缺失项：运行库在线下载并静默安装，可选组件解压到目标目录；
- 用 npm 安装 DSH 本体：`@deepseek-ai/dsh`，版本范围 `^0.1.5-rc.1`；
- 从启动器仓库取最新版启动器并铺到目标目录；
- 按你的勾选建桌面快捷方式、注册开机自启。

- Checks whether Node, pnpm, Git, Python and both runtimes are present.
- Fills the gaps: runtimes are downloaded and installed silently; optional components are unpacked into the target directory.
- Installs DSH itself via npm: `@deepseek-ai/dsh`, version range `^0.1.5-rc.1`.
- Fetches the newest launcher from its repository and deploys it.
- Creates the desktop shortcut and autostart entry you asked for.

Node 走官方源或国内镜像的 LTS（当前 22 线）。

Node comes from the official or China-mirror LTS line (currently the 22.x series).

---

## 安装包为什么这么小 / Why the package is small

安装包大约 **11 MB**。这不是靠砍功能换来的，而是靠「不搬运行时」和「不打包启动器」。

The package is roughly **11 MB**. That is not achieved by cutting features, but by not shipping runtimes and not bundling the launcher.

**运行库按需在线下载。** 如果机器上已经有 .NET 8 桌面运行时和 Windows App Runtime 1.8，就完全跳过；缺哪个补哪个。用户不必为了装一个工具先下载上百 MB 的完整运行时。

**Runtimes are fetched on demand.** If .NET 8 Desktop Runtime and Windows App Runtime 1.8 already exist, that step is skipped entirely; only what is missing gets installed. Nobody is forced to pull a hundred-plus megabytes of runtime just to install a tool.

**启动器走 manifest 现下最新版。** 安装器不内置启动器，而是安装时优先读取 `YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run` 仓库根目录的 `manifest.json`，拿不到时回退旧仓库 `YunxiRamito/DSH-Launcher`。这样启动器发新版时，安装器不需要重新发布。

**The launcher is resolved from a manifest.** The installer does not embed the launcher. At install time it reads `manifest.json` from `YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`, with `YunxiRamito/DSH-Launcher` as a transition fallback.

清单拉不到（断网、仓库不可用）时，回落到安装器自带的 `payload\launcher.zip`。

If the manifest cannot be reached (offline, repository unavailable), it falls back to the bundled `payload\launcher.zip`.

国内网络下清单渠道按可达性排序：

For networks in China, manifest sources are tried in order of reachability:

`ghproxy.net` > `gh-proxy.com` > jsDelivr > `raw.githubusercontent.com` > GitHub API

---

## 界面与选项 / Interface and options

界面**中英双语**，默认跟随系统语言，右上角可随时切换。

The UI is **bilingual (Chinese / English)**, follows the system language by default, and can be switched from the top-right corner at any time.

| 选项 / Option | 说明 / Notes |
| --- | --- |
| 安装范围 / Install scope | 仅当前用户 / 所有用户（All users 需要提权）/ Current user or all users (elevation needed for all users) |
| 可选组件 / Optional components | pnpm / Git / Python，均为解压到自选目录的便携安装 / Portable, unpacked into a directory you choose |
| 桌面快捷方式 / Desktop shortcut | 可选 / Optional |
| 开机自启 / Autostart | 可选，需要提权 / Optional, requires elevation |
| 安装后启动 / Launch after install | 默认勾选 / On by default |

### 提权只弹一次 / Exactly one UAC prompt

选「所有用户」或「开机自启」时，点下「安装」的那一刻会用 `runas` 起一个提权实例。向导里填过的所有选项会先落盘，再由提权实例读回来，因此无需重新执行向导，也无需重新填写任何内容。

When you choose "all users" or autostart, hitting **Install** spawns an elevated instance via `runas`. Everything you entered in the wizard is persisted to disk first and read back by the elevated instance, so you never have to redo the wizard or retype anything.

---

## 卸载 / Uninstall

安装器提供一个**独立的卸载程序 `DSH-Uninstall.exe`**，并注册到 Windows「应用和功能」，可以直接从系统设置里卸载。

A **standalone uninstaller, `DSH-Uninstall.exe`**, is installed and registered under Windows "Apps & features", so you can uninstall straight from Settings.

卸载时会询问要不要**连 Node 一起卸载**——因为 Node 是唯一写进系统的组件之一，有人只想留着自己用，有人想恢复原状。

Uninstall asks whether to **remove Node as well**, since Node is one of the few things written into the system: some people want to keep it, others want the machine back the way it was.

也支持命令行卸载：

Command-line uninstall is supported too:

```
DSH-Installer.exe --uninstall
```

---

## 命令行与无人值守安装 / Command line and unattended install

所有开关都可用于脚本部署：

Every switch is scriptable:

| 开关 / Switch | 作用 / Effect |
| --- | --- |
| `--silent` | 跳过向导直接安装 / Skip the wizard and install |
| `--uninstall` | 卸载模式 / Uninstall mode |
| `--scope=machine\|user` | 安装范围 / Install scope |
| `--source=china\|official` | 下载源 / Download source |
| `--dsh-root=<路径>` | DSH 本体目录 / DSH root directory |
| `--launcher-root=<路径>` | 启动器目录 / Launcher root directory |
| `--components-root=<路径>` | 可选组件目录 / Optional components root |
| `--git` `--pnpm` `--python` | 勾选对应组件 / Select those components |
| `--no-shortcut` | 不建桌面快捷方式 / No desktop shortcut |
| `--no-autostart` | 不注册开机自启 / No autostart |
| `--no-launch` | 安装完成后不启动 / Do not launch afterwards |
| `--no-runtime` | 不下载安装两个运行库（机器上已确认装好时用）/ Skip the runtime download-and-install step |
| `--report=<文件>` | 结果写 JSON / Write a JSON report |
| `--dry-run` | 演练，不落盘 / Dry run, nothing is written |
| `--page=N` | 跳页预览，强制演练模式 / Jump to page N for preview, forces dry-run |

示例 / Example:

```powershell
# 静默全用户安装，走国内源，装 pnpm 和 Git，结果写 JSON
.\DSH-Installer-Setup.exe --silent --scope=machine --source=china --pnpm --git --report=install.json

# 只预览第 4 页长什么样，不写任何文件
.\DSH-Installer-Setup.exe --page=4
```

---

## 未签名与 SmartScreen / Unsigned build and SmartScreen

**当前发布版本未做数字签名。** 所以首次运行时 Windows SmartScreen 会弹出「Windows 已保护你的电脑」。

**Current releases are not code-signed.** Windows SmartScreen will therefore show "Windows protected your PC" on first run.

这是未签名开源软件的**预期行为**，并不代表文件有害。两种办法过掉：

This is the **expected behaviour** for unsigned open-source software and does not indicate a harmful file. Two ways past it:

- 在「Windows 已保护你的电脑」对话框点「更多信息」→「仍要运行」；
- 或右键 exe →「属性」→ 在「常规」页勾选「解除锁定」→「确定」。

- In the "Windows protected your PC" dialog, click **More info** → **Run anyway**.
- Or right-click the exe → **Properties** → tick **Unblock** on the General tab → **OK**.

如果你更放心从源码来，可以自己按下面的步骤构建，产出的是同一个程序。

If you would rather build it yourself, follow the instructions below — you get the same program.

---

## 日志与排障 / Logs and troubleshooting

| 文件 / File | 内容 / Contents |
| --- | --- |
| `%LOCALAPPDATA%\DeepSeekHarness\installer.log` | 安装日志 / Installer log |
| `%LOCALAPPDATA%\DeepSeekHarness\installer-state.json` | 安装状态记录 / Installer state |
| `%TEMP%\dsh-boot.log` | 引导阶段日志 / Bootstrapper log |

出现问题时，提供上述几个文件即可定位大多数情况。

When something goes wrong, sending those files along usually identifies the cause.

快速自查 / Quick self-check:

- 卡在欢迎页 → 系统版本低于 build 17763 / Stuck on the welcome page → OS older than build 17763.
- 运行库下载失败 → 试试 `--source=china`，或自行手动装好两个运行库再重跑 / Runtime download failed → try `--source=china`, or install both runtimes manually and rerun.
- 想先看看会发生什么 → `--dry-run` 或 `--page=N`，都不落盘 / Want a preview → `--dry-run` or `--page=N`; neither writes anything.

---

## 从源码构建 / Building from source

需要 / Requires:

- .NET 8 SDK
- Windows App SDK 1.8

```powershell
# 只编译共享库 / Build the shared library only
dotnet build src\DshInstaller.Shared\DshInstaller.Shared.csproj -c Release

# 开发用发布：输出到 dist\preview 并打 zip
# Dev publish: output to dist\preview and zip it
.\pack-preview.ps1

# 正式打包：产出单个 dist\DSH-Installer-Setup.exe，并拷到桌面
# Release packaging: produce a single dist\DSH-Installer-Setup.exe and copy it to the desktop
.\pack-release.ps1
```

---

## 目录结构 / Repository layout

根目录为 `DSH Installer\` / The project root is `DSH Installer\`:

| 路径 / Path | 内容 / Contents |
| --- | --- |
| `src\DshInstaller\` | WinUI 3 主程序；向导页面在 `Pages\`，自绘控件在 `Controls\`，文案在 `Localization.cs` / WinUI 3 app; wizard pages in `Pages\`, custom controls in `Controls\`, strings in `Localization.cs` |
| `src\DshInstaller.Shared\` | 共享库：检测（`Detection\`）、下载 / 解包 / 安装编排 / 卸载（`Install\`）/ Shared library: detection (`Detection\`), download / unpack / install orchestration / uninstall (`Install\`) |
| `boot\Boot.cs` | 引导程序源码，编译成 `Setup.exe` 外层引导 / Bootstrapper source, compiled into the outer `Setup.exe` |
| `boot\Uninstall.cs` | 独立卸载程序源码 / Standalone uninstaller source |
| `assets\DSHInstaller.ico` | 图标 / Icon |
| `docs\` | `ARCHITECTURE.md` / `SETUP.md` / `CHANGELOG.md` |
| `STATUS.md` | 开发交接文档 / Development handover notes |
| `pack-preview.ps1` `pack-release.ps1` `build.ps1` | 构建脚本 / Build scripts |

启动器是**另一个独立仓库**（`YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`）。本安装器只负责把它正确地铺到目标目录。

The launcher lives in a **separate repository** (`YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`). This installer is only responsible for deploying it correctly.

---

## 常见问题 / FAQ

**安装完成后启动器位于何处？**
在安装时选择的目标目录中，安装器会将启动器部署到该处。如需单独指定，使用 `--launcher-root=`。安装完成后从托盘图标或快捷方式启动即可。

**Where does the launcher end up?**
In the target directory you chose during install — the installer deploys it there. Use `--launcher-root=` to point it somewhere specific. Afterwards, start it from the tray icon or the shortcut.

**能不能离线安装？**
不能完全保证。两个运行库是安装时在线下载的，DSH 本体也走 npm。如果机器上已经装好这两个运行库、并且有一个可用的 Node，安装器就不需要联网；启动器也有自带的 `payload\launcher.zip` 作回落。

**Can I install offline?**
Not with any guarantee. Both runtimes are downloaded at install time and DSH itself comes from npm. If the two runtimes and a working Node are already present, no network is needed, and the launcher falls back to the bundled `payload\launcher.zip`.

**如何安装到其他位置？**
可通过 `--dsh-root=` / `--launcher-root=` / `--components-root=` 分别指定，也可在向导中直接修改目标路径。

**How do I change the install location?**
Use `--dsh-root=` / `--launcher-root=` / `--components-root=`, or just edit the target paths in the wizard.

**为什么需要管理员权限？**
只有在选「所有用户」或「开机自启」时才需要——前者要往系统范围写，后者要注册开机启动。选当前用户、不勾自启的话，全程不需要提权，只弹一次 UAC 也仅限于这两种情况。

**Why does it need administrator rights?**
Only for "all users" (system-wide writes) or autostart (registering a startup entry). With the current-user scope and autostart off, no elevation is needed at all; the single UAC prompt only appears for those two choices.

**Node 会不会污染系统？**
Node 是唯一写进系统的组件之一（另一个是两个运行库）。它按标准方式安装、不修改你的项目环境；如果不想留着，卸载时选择连 Node 一起卸载即可。

**Does Node pollute my system?**
Node is one of the few components written into the system (the other being the two runtimes). It installs the standard way and does not touch your project environments; if you do not want to keep it, choose to remove Node during uninstall.

**怎么彻底删干净？**
先跑卸载程序（或 `DSH-Installer.exe --uninstall`），卸载里选择一并移除 Node；然后删掉你选择的安装目录（可选组件是便携解压的，删目录即可）。最后如果想清日志，可以手动删除 `%LOCALAPPDATA%\DeepSeekHarness\`。

**How do I remove everything cleanly?**
Run the uninstaller (or `DSH-Installer.exe --uninstall`) and opt to remove Node as well; then delete the install directory you chose (portable components go away with the directory). To clear logs too, delete `%LOCALAPPDATA%\DeepSeekHarness\` by hand.

---

## 许可 / License

MIT License. 详见仓库中的 `LICENSE`。 / MIT License — see `LICENSE` in the repository.

## 相关仓库 / Related repositories

- 启动器 / Launcher: [`YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run) — 托盘程序，本安装器负责铺装 / the tray app this installer deploys
- DSH 本体 / DSH itself: npm 包 `@deepseek-ai/dsh` / the npm package `@deepseek-ai/dsh`

感谢 DeepSeek Harness 与 Windows App SDK 社区的工作。

Thanks to the DeepSeek Harness and Windows App SDK communities.

---

## Code signing policy

**代码签名政策**

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Committers and reviewers / 提交与审查: [YunxiRamito](https://github.com/YunxiRamito)
- Approvers / 批准人: [YunxiRamito](https://github.com/YunxiRamito)

**Privacy policy / 隐私政策**: 本安装程序不收集、不上传用户数据;它只在你确认之后从官方来源下载运行库与 DSH 本体。

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.
