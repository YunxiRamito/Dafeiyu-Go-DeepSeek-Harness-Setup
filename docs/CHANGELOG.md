# 变更记录

本文件按 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 维护：一个版本一节、最新在上，
日期用 `YYYY-MM-DD`，改动按 Added / Changed / Fixed / Security 分类。
写给用户看：说人话，只讲用户在乎的事，不写内部实现。
安装器和 DSH 启动器同步发版，所以同一节里两边的改动都写。

## [1.5.2] - 2026-09-30

**安装器 + 启动器**

安装器本体这一版**没有功能改动** —— 纯粹是为了跟启动器 v1.5.2 对上号：启动器更新时会核对安装器 Release 的版本，对不上就拒绝更新，所以两边必须同版本发。

### Added

- 主页「Token 用量与余额」重做：柱状图 = 每天用了多少 token，折线 = 当天花了多少钱，**共用一条 x 轴**，带坐标轴（左轴 tokens、右轴金额、底部日期刻度）。右上角可切「近 7 / 15 / 30 天」；鼠标停在图上会高亮那一天，弹出当天的用量、花费和调用次数。
- 花费按内置价格表估算，**高峰与空闲两档价都算**：高峰 = 工作日 9:00–12:00、14:00–18:00，其余时段（含周末和法定节假日全天）按空闲价，空闲价是高峰价的一半。价格表在 `%LOCALAPPDATA%\DeepSeekHarness\model-prices.json`，DeepSeek 调价时改这个文件就行，不用等发版。
- 卡片上多一行「余额实扣」：按账户余额的变化推算真实扣费，跟估算曲线对照着看就知道差多少。

### Changed

- 卡片上只留「账户余额」和「今日」，其余明细（近 30 天、输入输出、缓存命中率、最多模型、最近调用）收进悬停提示 —— 以前是一屏文字。
- 「安装用量统计插件」改成安装启动器**自带**的插件，不再从社区装。

### Fixed

- 修复社区那只用量统计插件跟 DSH 0.2.x 不兼容：它把 `@deepseek-ai/dsh-home-paths` 写死在 `peerDependencies` 里，DSH 的兼容性校验会直接拦下并提示「可能导致崩溃」。现在用启动器自带的 `dsh-token-stats`：零依赖、不声明 peerDependencies、离线可装；安装时会顺手把不兼容的旧插件条目摘掉。
- 修复装了用量统计插件、但还没产生记录时，卡片一直劝你去装插件（看起来就像「老是说未安装」）。

## [1.5.1] - 2026-09-29

**安装器 + 启动器**

这一版两边都有改动。

### Added

- 卸载前多了一步「导出数据」：可以挑要带走的内容（默认配置 + 技能 + 插件），选个文件夹导成 `.dym` 备份包，之后在新机器上重装时再导入回去。不想要就点下一步跳过。
- 装完之后多了一步「导入原有数据」：有以前导出的 `.dym` 备份，就能把配置、技能、插件一次性搬回来，恢复过程会逐个文件报进度（「正在恢复 技能 (2/12) · …」）；碰到同名文件可以选覆盖、跳过或两个都留。没有备份，点一下「下一步」跳过即可。
- 新增「版本」页：DSH 本体可以自己挑版本了（默认 / 最新正式版 / 最新预览版 / 最新内测版 / 手动填版本号）。以前版本写死在安装器里，想试预览版只能等我们发新版。启动器那条版本通道暂未开放，界面上是禁用的。
- 新增「安装清单」与完整性校验：装完之后会把这次铺下去的程序文件记一份快照（大小 + 哈希），修复安装时逐项核对，缺的、被改坏的会直接点出来。升级过的机器会判成「快照过期」并重新生成，不会误报成文件损坏。
- 插件页多了「插件自检」：查一遍已装插件是不是真的装上了（文件在不在、链接的目录还在不在、该进启动清单的有没有进），有问题当场列出来。
- 插件页多了「一键修复」：按插件清单重装一遍依赖，再自检一次。修不好就把包管理器返回的原话直接摆出来。
- 主页公告下面多了「Token 用量与余额」：今日 / 近 7 天 / 近 30 天的 Token 用量、输入输出、缓存命中率、用得最多的模型，加上账户余额。逐日用量需要「用量统计插件」，没装的话卡片里有一键安装。

### Changed

- 「自动检查周期」多了「关闭」；启动器、DSH 本体、插件三条更新通道各自都能关掉，界面上写明白了。
- 更新失败的通知、自动更新完成的通知，不再跟着「更新提醒」开关一起被静默。
- 「修复安装」挪到了「开始安装」左边，和安装并排；本机没有安装痕迹时它是灰的（以前要点下去才告诉你「没有安装记录」）。

### Fixed

- 修复安装推荐插件时「装了一半」的残留：以前某个插件失败，已经写进启动清单的条目会留在那里、依赖却没装完，安装器这边显示「已跳过」，DSH 下次启动却报无法解析。现在装之前先给清单留底，失败或被取消就还原，装完再逐个确认文件真的落地了。
- 修复插件安装失败只显示「退出码 1」：现在会把包管理器自己那句话（比如找不到匹配的版本）一并显示出来。
- 修复插件没装上却显示「已安装」：安装失败会把写进启动清单的条目撤回，并显示包管理器给出的原因（以前只留下一句「退出码 = 1」）。
- 修复「装完重启 DSH 报无法解析」：安装完成会校验文件真的落在插件目录里，不通过就算失败。

## [1.5.0] - 2026-09-27

**安装器**

### Added

- 装不上时可以直接导出安装日志：点一下打成一个压缩包并告诉你存在哪，转发给客服就行。
- 安装前多了一份体检：磁盘剩余空间、系统版本、本机是否已装过 DSH、8787 端口被谁占着、是否走代理。有问题的项会标黄，并附一句该怎么办。
- 新增「修复安装」：文件不见了或被杀软删掉了，点一下自动补齐，不用重装，也不会动你的设置和数据。命令行同样支持 `--repair`。
- 下载源页面可以直接设置代理：不使用、跟随系统、自定义（http / https / socks5 + 地址端口），设置会记住。
- 组件页新增「强制重新下载组件」开关：想统一用便携版时勾选。

### Changed

- 产品名统一为 **Dafeiyu-Go**：标题栏、欢迎页、完成页、卸载页，以及文件属性里的产品名和描述都换了。
- 本机已经有较新版本的 Node / Git / pnpm / Python 时直接使用，不再重复下载；组件页会写明「已检测到，将直接使用」。
- 卸载默认保留设置、技能、插件和会话记录，重装即可接着用；取消勾选才会一并清除。
- 部署启动器不再等待镜像同步：以前可能白等二十多秒，现在没同步就立刻换源。
- 多线程下载更聪明：首选下载源不可用时会换到一条支持多线程的源，而不是退回单线程。

### Fixed

- 修复卸载会连用户数据一起删掉的问题：便携安装下技能、插件和会话就放在程序目录里，以前会被一起清掉。
- 修复代理设置不生效的问题：插件列表，以及安装过程中调用 npm / pnpm / git 的步骤，现在都按你选的代理走。
- 修复导出安装日志失败的问题。

### Security

- 导出的日志会逐行抹掉令牌、密码、密钥这类内容；文件名里带 token / key / secret 的文件一律不打包。

### DSH 启动器

这一版安装器和启动器一起发，下面是启动器的改动。

**新增**

- 新增「主页」：打开设置先看到它，一眼看清 DSH 是不是在运行、有没有新版本，常用的插件、技能、组件、更新入口也都摆在这儿。
- 新增「公告」栏：新消息直接显示在主页上，没看过会打「有新公告」的标记，看完点「全部已读」。
- 新增「技能」板块：官方推荐、在线技能、本地技能三页，装技能跟装插件一样简单，支持从 GitHub 仓库或本地压缩包安装。
- 本地技能能停用、能删除，不用再去翻文件夹。
- 装了技能可以「检查更新」「全部更新」，装过的都记得来自哪个仓库。
- 「关于」页多了更新日志，直接看这一版改了什么。
- 加速源可以自己挑：点「高级设置」能看到每个源的实际速度，也能指定用哪个；默认自动选最快的。

**改进**

- 下载速度明显变快：插件和技能包同时开几条通道下，进度条旁边显示实时速度。
- 启动时自动测一遍各加速源，网络环境变了（换 Wi-Fi、连 VPN、改代理）会重新测。
- DSH 本体的更新通道可选自动 / latest / next / alpha，修好「明明有新版本却检查不到」。
- 启动器更新分正式版和预览版，国内更新统一走国内镜像。
- 更新进度不再乱跳：按这次实际新增的体积算，不再出现 223/230 这种数字。
- 界面文字整体精简，按钮改成明确的动作词，空列表和报错都会说下一步干什么。
- 插件和技能卡片显示仓库头像和星标。

**修复**

- 修复代理设置重启后被改回「不使用代理」的问题。
- 修复更新检查不到新版本、更新进度看着像卡住的问题。
- 修复下载进度不动、或者没有进度直接跳到完成的问题。
- 修复插件安装失败却只说一句「安装失败」的问题，现在会写清失败原因。
- 修复个别插件装不上的问题（压缩包里有个别文件解不开会让整包失败，现在跳过继续装）。
- 修复国内装插件卡在「安装中」很久不动的问题（现在会走国内镜像）。
- 修复插件明明装好了、重启后却显示「没安装」的问题。
- 修复「检查更新」点完没有任何提示的问题，结果直接显示在页面上。
- 修复技能列表「只看中文内容」选了没效果的问题。
- 修复技能页显示一百多条结果却只有一页的问题。
- 修复「已验证」筛选看着像坏了的问题：现在会写清"市场里的项目本来就都验证过"，并加了「未验证」筛选。
- 修复 Windows 10 下插件图标线条过粗、技能图标不统一的问题。
- 修复删除技能偶尔失败、让人以为按钮没反应的问题，失败也会给出原因。

### Installer (English)

- **Added** — Export the installation log in one click when something goes wrong; it is saved as a zip and you are told where. A pre-install check now covers disk space, Windows version, an existing DSH installation, port 8787 and the network proxy, flagging anything risky with what to do about it. A new "Repair installation" restores missing files without reinstalling and without touching your settings or data (also `--repair` on the command line). The download source page now offers a proxy setting (no proxy / system proxy / custom http, https or socks5) and remembers it. The components page has a "Re-download components" switch.
- **Changed** — The product name is now Dafeiyu-Go everywhere, including the window title and file properties. Components already installed on this PC are reused instead of downloaded again. Uninstalling keeps your settings, skills, plug-ins and sessions by default. Deploying the launcher no longer waits for a mirror to catch up. Multi-threaded downloads now switch to a source that supports segmented transfer when the first one is unavailable.
- **Fixed** — Uninstalling no longer removes your data along with the program. The proxy setting now applies to the plug-in list and to the npm / pnpm / git steps. Exporting the installation log no longer fails.
- **Security** — Exported logs have tokens, passwords and keys masked line by line, and files whose names contain token, key or secret are never included.

### DSH Launcher (English)

- **Added** — A new Home page shows whether DSH is running and whether an update is available, with the usual plug-in, skill, component and update entries. Announcements appear on the Home page with an unread marker and a mark-all-read action. A new Skills section (featured, online, local) installs skills from a GitHub repository or a local archive, lets you disable or delete local ones, and supports check-for-updates and update-all. The About page shows the changelog. Acceleration sources can be picked manually from Advanced settings, which lists the measured speed of each source; automatic is the default.
- **Changed** — Plug-ins and skill packages download over several connections at once, with the live speed shown next to the progress bar. Source speeds are measured at startup and re-measured when the network changes. The DSH core update channel can be automatic, latest, next or alpha. Launcher updates are split into stable and preview, and use mainland mirrors in China. Update progress is calculated from the bytes actually added, so it no longer jumps. Interface wording was shortened and buttons now use action verbs. Plug-in and skill cards show the repository avatar and star count.
- **Fixed** — The proxy setting is no longer reset to "no proxy" after a restart. Update checks and update progress no longer appear stuck. Download progress no longer stalls or jumps straight to complete. Plug-in failures now state the reason instead of a bare "installation failed". Individual files that cannot be extracted no longer fail the whole plug-in. Installing plug-ins in China no longer sits at "installing" for minutes. An installed plug-in no longer shows as not installed after a restart. "Check for updates" now always reports its result. The skills list honours the Chinese-only filter and shows more than one page. The "verified" filter now explains that marketplace entries are verified by default and adds an "unverified" filter. Plug-in icon strokes on Windows 10 and inconsistent skill icons were corrected. Deleting a skill no longer fails silently.

## [1.4.9.1] - 2026-09-23

### 安装程序

- 新增“推荐插件”向导页，读取官方 `featured-plugins.json`，支持全选、跳过和逐项选择。
- 推荐插件卡片显示仓库图标；逐项显示正在下载和正在安装的插件名称及进度。
- 推荐插件改为和启动器一致的 GitHub tarball 下载流程：解压到 `plugins\<repo>`、写入 profile `link:`、执行 pnpm install，不再依赖 Git SSH。
- 插件下载使用 4 线程；Git、Python、pnpm、Node 等其他大文件继续使用 8 线程。
- GitHub 镜像池统一为 `gh-proxy.com`、`ghproxy.net`、`ghfast.top`；大陆 CDN 与官方下载严格分流。
- 下载源选择移到欢迎页后的独立页面，可选大陆 CDN 或官方下载。
- 卸载时清理 `%LOCALAPPDATA%\DeepSeekHarness\Boot`。
- 修复完成页“安装后立即打开启动器”勾选后不生效的问题。
- 修复取消安装后回滚沿用已取消令牌，导致所有清理步骤被跳过的问题。
- 修复顶层安装目录未被记录为“本次创建”，导致取消回滚后 `C:\Program Files\DeepSeek Harness` 残留的问题。
- 取消信号现在会传递给 pnpm/git 子进程。
- 更新顺序与启动器对齐：安装器、卸载器和启动器统一为 `1.4.9.1`。

## [1.4.9] - 2026-09-22

**Dafeiyu-Go 过渡升级版本**

> 本版合并了此前未正式发布的 `1.4.3` 功能。

### DSH 启动器

- 产品更名为“大肥鱼Go / Dafeiyu-Go”，安装器与启动器仓库完成新名称迁移。
- 新增代理设置，可接入本地 Proxy 软件，改善中国大陆网络环境下的下载与访问体验。
- 新增 DSH 插件板块，支持分类、搜索、排序、分页和缓存，并支持插件详情、安装进度、本地目录、卸载、单个更新和全部更新。
- 新增组件板块，支持检测和安装 .NET、Windows App Runtime、Node、MinGit、pnpm、Python。
- 修复若干已知问题并提升稳定性。

### 安装程序

- 产品更名为“大肥鱼Go / Dafeiyu-Go”，安装器与启动器仓库完成新名称迁移。
- 安装器升级 Mica Alt，并跟随 Windows 主机亮暗主题；不支持时自动回退 Acrylic。
- 修复组件页、步骤条、卡片、完成页和卸载页的深色主题适配。

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
