# 交接:大肥鱼Go / Dafeiyu-Go

## 2026-10-08 完整 DSH 本地部署与立即 UAC（优先于历史）

- 最终测试包 `dist/test-1.7.0-local-dsh-tray-bans-20261008/DSH-Installer-Setup.exe`，1.7.0.0，11,441,991 字节，SHA-256 `398B4C8B941635CA8DE114B1EBAB133462019532F7482175F8084CCFA08D0F22`。外层 `boot/Boot.manifest` 为 requireAdministrator，最终EXE嵌入资源已验证，双击立即UAC；缺少运行库仍使用现有下载/安装窗口。
- 默认旧范围改官方 latest 并先解析精确版本，后端源改“备用”并说明可能稍慢但适配大部分国内连接。后端源本体不执行 npm install，完整 Windows x64 / Node 22 ZIP 经SHA、安全路径、CLI/原生依赖离线验证后事务提交；Node复用只允许22.x。服务器已提供 `0.2.0-rc.2` / `0.2.1-alpha.1` 两个包。已有DSH沿用复用，不强制升级；测试新本体用新目录。
- 推荐插件仍单独联网，tar.gz 改 .NET UTF-8/PAX（跳过全局元数据头），pnpm只装生产依赖；插件失败回退该次profile并显示完成页警告，继续其它插件。每步日志含耗时，Observer/ProcessRunner重复落日志已消除。PATH同步广播约14秒未修改。
- [验收报告](../preview-artifacts/1.7.0-local-dsh-tray-bans-20261008/validation.md)：121个源码输入、45个payload、EXE附加ZIP和UAC资源检查通过；故障/归档25项、bundle纯回归60项、两个真实包各63项；线上完整本体隔离部署114.4秒、实际失败归档解出68个中文Markdown。未执行真实安装/卸载/UAC/PATH变更，未正式发布。
- 公开启动器manifest仍为1.6.0；本轮启动器封禁/通知/托盘修复请用 `../Dafeiyu-Go-DeepSeek-Harness-Click-To-Run/DeepSeekHarness-1.7.0-local-dsh-tray-bans-review.zip` 测试。日志在 `%LOCALAPPDATA%\DeepSeekHarness\installer.log`（同目录ui/crash），Boot日志 `%TEMP%\dsh-boot.log`。

## 2026-10-08 安装失败与后端下载修复（当前安装器/服务端，以本节为准）

用户提供 `Dafeiyu-Go-logs-20261008-142359.zip`，已由 `installer_backend_audit` 子智能体专项自查，主任务完成服务器诊断、JSON/回滚修复、构建和部署。

- 新 [安装器复测 EXE](<G:/DeepSeek DSH/DSH Works/Project/Dafeiyu-Go/Dafeiyu-Go-DeepSeek-Harness-Setup/dist/test-1.7.0-backend-repair-20261008/DSH-Installer-Setup.exe>)：1.7.0.0，11,434,433 字节，SHA-256 `A211CD51B12E262B4B96E433289AAECA770B77738B5647A603C032774F4FC466`。此前 download-fixes 测试包保留作历史，复测使用此包。运行库仍走普通窗口检测、缺少时按需下载/安装；未正式发布版本。
- 已修复 Node 从 C 盘临时目录部署到其它盘的 Directory.Move 错误；npm 内 ZIP SHA 不匹配时严格校验并换独立发行源；官方兜底不再被再次包装成同一后端；缓存阶段显示真实字节/总量/速度，连续60秒无增长换源，8秒响应头/元数据限时并可取消。
- 推荐插件与安装清单 JSON writer 增加 TypeInfoResolver；回滚只清理本次创建的目录，空路径跳过；正在运行的 Boot payload 由引导程序在安装器退出后清理。
- 服务器原写死代理对 Node/raw/npm/Python 超时而直连正常，已移除过时默认，保留显式代理配置；连接10秒、头20秒限时，补 download.microsoft.com 官方运行库跳转白名单。新生产镜像 `sha256:4cc21790e9226124d66f7c8a66853aa3267359316e4c9ca9d07d7ac86b5012f8`，备份 `/home/dafeiyu/developer-center-backups/download-repair-20261008t063711z`，含回滚脚本。数据库/反馈、凭据、TLS和缓存卷未变更，API/db healthy、TLS running。
- 验证：客户端下载51项、JSON/回滚10项、服务端下载48项通过；安装器/服务端 Release 都是0警告/错误。116个安装器输入哈希及45个附加payload文件校验通过。实际后端Node清单约1至2秒，35.6MB冷缓存约10秒，完整本机下载21.7秒/约1.64MB/s且SHA正确；他人网络速度仍需复测。
- 完整证据和复现见 [本轮故障验收](<G:/DeepSeek DSH/DSH Works/Project/Dafeiyu-Go/preview-artifacts/1.7.0-installer-backend-repair-20261008/validation.md>)。原生真实安装/卸载/UAC尚未执行；正式manifest/tag/Release没有变化。启动器仍使用14:19的最新feedback-review ZIP，SHA `AF6C6C3D853ED58B31DA43F01D4CD9951F2860DF9C54F8037A91AB3CD2ECA256`。

> 给下一个接手的人(或下一个 AI)。当前源码和本地测试包版本为 `1.7.0.0`，尚未正式发布；公开历史版本为 `1.6.0`（2026-10-07 已发布，公开版本以 Releases 为准）。启动器与安装器已解耦，不再要求两边版本号相同。
> 第一入口请先读父级 `G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\HANDOVER.md`，再读本文件、
> `STATUS.md`、`HANDOVER-1.5.0-installer.md`、`RELEASE.md` 和 `TRANSITION.md`。

最后更新:2026-10-08（1.7.0 本地测试包；公开 1.6.0）

## 2026-10-08 安装器测试包与引导流程

- 测试入口：`dist/test-1.7.0-download-fixes-20261008/DSH-Installer-Setup.exe`，1.7.0.0，11,432,005 字节，SHA-256 `1E50347934A332906E38A011EEF16277E0D262B8A0C7880F9BC1214FA6B91DC3`。未创建正式 Release/tag 或更新公开 manifest。
- 沿用现有框架依赖发布：Boot 启动时检查 .NET 桌面运行时和 Windows App Runtime，缺少时显示普通下载/安装窗口，需要安装权限时申请提权，准备完成后启动向导。用户本轮追问确认了这一引导方式，测试包没有改成捆绑运行库的自包含包。
- 使用父级 `preview-artifacts/1.7.0-download-fixes-20261008/Build-Installer.ps1` 的隔离快照构建，111 个输入哈希一致，Release 0 警告/错误，附加 payload 的 45 个文件校验通过。尚未执行真实安装、卸载和 UAC 流程。
- 本轮新增反馈图片和封禁功能由独立的最新启动器检查 ZIP 测试；本安装器测试包不会替代启动器的反馈功能验收。

## 2026-10-08 未发布补充：来源继承

安装器成功收尾校验后将 `SourcePreference` 保存到 `installer-state.json`、安装清单和卸载注册表，并在实际启动器目录保存 `installer-defaults.json`。默认文件只含 schema、绝对 DSH 路径和已知来源，机器范围或自定义组件安装不依赖安装器账号的 LocalAppData。首次创建启动器设置读取该默认，再回退路径匹配的本地状态；`china=>Accelerated+Auto`、`backend=>Accelerated+backend`、`official=>Official+Auto`。已有用户设置不被修复或默认文件覆盖。

安装器会话和修复恢复保存的来源；静默支持 `--source=china|backend|official`，修复显式来源参数优先于保存记录。ConfigStore 测试可用 `DAFEIYU_INSTALLER_SETTINGS_DIRECTORY` 隔离。InstallerSourceDefaults 35 项与 ProxyScope 32 项通过；Probe 增加4项修复有效来源断言，等待主任务完整构建。此补充未提交、未推送、未发布，不表示公开安装器版本变化。

## 1.6.0 本轮交接（2026-10-07 已发布）

**发布记录**：`main` 已推送（`863833d`）、标签 `v1.6.0` 已推送；
Release https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.6.0
资产 `DSH-Installer-Setup.exe` 11,439,334 字节（文件版本 1.6.0.0），
线上摘要 `sha256:e0a3b660e4a1ca614a257ea24e8c3a4ca4ef6f9ff729f0a2a7b3da512c9a8b9e` = 本地一致。
`releases/latest` 现在返回 `v1.6.0`，启动器的安装器检查（`InstallerUpdateService` 读该接口的
`DSH-Installer-Setup.exe` 与资产 `digest` 校验）能正常比对与更新。
> 注意：启动器 v1.6.0 发布时安装器这一侧还没发，旧版启动器/安装器「两边同 tag」的老流程会因此报错；
> 安装器 v1.6.0 发布后该问题消失。

按父级 `DS41-实施任务.md` 执行。

### 安装器仓库改了什么

- `src/DshInstaller.Shared/WellKnown.cs`：删除 `LauncherVersion = "1.4.9.1"`。
  它没有任何读取方，却让人以为"安装器版本必须等于启动器版本"，和本轮的解耦方向相反。
- `Directory.Build.props` / `app.manifest`：`set-version.ps1 -Version 1.6.0` 同步版本。
- 核对两仓 CI：安装器只校验自家 `Directory.Build.props` 版本 vs 自家 tag；
  启动器只校验自家 csproj 版本 vs 自家 tag；启动器下载地址来自它自己仓库的
  `manifest.json` 的 `release.Version`，不拿安装器版本拼。**本来就没有跨仓 tag 耦合**，
  所以不需要改工作流。

### 与启动器的边界（新）

- 启动器不再在更新自己之前强制把安装器升到同一版本。它读安装器 Release 的最新版，
  和自己的本地安装器版本比：远端更新才替换，否则跳过。
- 安装器检查失败 / 替换失败 / Release 没给 SHA-256，启动器只提示并保留当前安装器，
  **不阻断启动器自身更新**。
- 本地版本读取顺序：`%LOCALAPPDATA%\DeepSeekHarness\installer-state.json` →
  `<DSH 根>\dsh\install-manifest.json` 的 `versions.installer` → 卸载注册表
  `DisplayVersion` → `.installer\DSH-Installer.exe` / `DSH-Uninstall.exe` 文件版本。
- 读不到版本时不无条件重装；启动器设置页「更新」里有「修复安装器」作为显式修复入口。

### 验证

- 本仓库 `DshInstaller.Shared` 与 `DshInstaller` Release/x64 构建 0 错误。
- 安装器侧的判定逻辑由启动器仓库的 `tests/UpdateIntegrity.Regression` 覆盖（49 项）。
- **未测**：完整安装 / 卸载 e2e、UAC 提权、真机覆盖升级。本轮不推包。

## 1.5.4 已发布交接（2026-10-06）

- 本轮同步余额 / Token 图表、下载中心与代理生效范围设置的产品更新说明；安装器仅配套同步版本，未新增安装 / 卸载流程功能。
- 最终本地验证：安装器 30 项离线计划回归通过，Release 探针构建 0 警告/0 错误，pack-release.ps1 -NoDesktop 完成约 10.9 MB 配套安装包。启动器 BalanceUsage 32、ProxyScope 32、DownloadTasks 34、LauncherSafety 61、UpdateIntegrity 11 全通过，真实 DSH 代理层 3 个本地请求通过，Release/x64 构建通过。
- 已独立修复余额账户/币种/日期隔离、DSH 不支持代理的显式拒绝、更新任务目录互删与分片线程初始化竞态。隔离启动器预览点击范围开关保存及重载通过，下载空页面入口可见；图表视觉/DPI、完整下载 GUI 点击、UAC、完整安装卸载 e2e 未测，符号链接权限用例跳过。正式资产及镜像已核验，清单使用 CI 正式资产哈希。
- 发布按 `RELEASE.md` 走新 tag CI，先安装器后启动器；正式哈希只采用实际 CI Release 资产。两仓资产及 npm / npmmirror 包核验完成后才更新启动器 manifest，两个 Release 正文均须补产品日志并读回核验。
- 以下章节为历史记录；公开清单已核验为 1.5.4。

- 正式发布：安装器 CI `37423542360`（commit `88c7e8f`）成功，SHA-256 `f5baaca934311a13056ff62dd3c7356ff686687b5eb18654ed7e1adefac7625f`；启动器 CI `37423773769`（commit `dd5c448`）成功，SHA-256 `2a0e93e8c078c6f648582b29048e2a07745a4a8038cbe825b39f9c8d68843fb8`。两包未签名，正式 ZIP 中引导与 Core 的 FileVersion 均为 1.5.4.0，签名状态 NotSigned。
- npm 与 npmmirror 实际 tgz 完全一致，SHA-256 `5b9fc39432d9efd3bd662da2b91350a71ee22968666aa75d76b8ec232913aa92`；包内 ZIP 与正式启动器资产一致。两个 GitHub Release 标题与产品更新正文已 PATCH 并 GET 读回核验。
- 清单已提交 `d8b6bd6` 并推送 main；官方 raw main、jsDelivr main（purge 后）与固定 d8b6bd6 清单均读回 1.5.4，SHA-256 与正式启动器 ZIP 一致。

## v1.5.3 发布交接（2026-10-04）

- 安装器 CI `37179657388`（commit `abf3a0a`）成功，已下载核验 SHA256 `69e770e068c997fec62956ce362250cba0cfd12d64491f96b03c1f943e7f9b88`；启动器 CI `37179772669`（commit `950d354`）成功，已下载核验 SHA256 `56a21d1057a392a4b5b24c3f2d74aa34fc98d529242dc02a188a5332045c66ec`。
- npm 1.5.3 与 npmmirror 均已就绪（HTTP 200）；下载 tgz 完全一致，SHA256 `1d0af60ab658f15ecc216b02cfc7408be30e5d6747b478fdb1d57d87f8d4a9f5`，内含 ZIP 哈希与官方一致。
- 版本内容：只读健康预览与白名单 JSON、安装计划、tar.gz 安全解包、保留数据保护、修复范围/自定义目录、Node 目录边界与缺失/无效 SHA256 停止更新；README 历史移出首页、使用真实宣传图、移除受版本控制的过时 1.3.21 归档。
- 回归 61 + 11 + 30 项及 Release 构建通过；GUI、UAC、完整安装/卸载 e2e 未测；符号链接用例因 Windows 权限不足跳过。不保证所有安全问题均已解决或完整事务回滚。
- 本轮仅收尾发布文档，未提交或推送；仓库提交由父级流程负责。

## 当前文档维护约定

- 本轮仅整理 `README.md`、新增 `docs/README.en.md` 并更新本仓库交接/状态入口；未修改应用源码、未构建发布包、未提交或推送。启动器由另一任务负责，不在本轮编辑范围。
- 中文 README 是用户入口，英文单列；详细 CLI/构建折叠。流程图是示意，不是截图；不要用虚构 UI 充当产品截图。
- 源码核对：Node 使用便携 ZIP；PATH/卸载注册/快捷方式/任务/运行库仍会改动系统。管理员权限还涉及运行库与受保护目录，不能承诺仅两个条件提权或只一次 UAC。
- `BuiltInSteps.RunLauncherAsync` 没有本地 `payload\\launcher.zip` 回退；断网不能保证安装。`--dry-run` / `--page` 不是零写盘，外层 Boot 仍会解压、记录日志并按需补装运行库；`--no-runtime` 不禁用 Boot。
- **防膨胀**：更新事实请替换当前入口的对应条目，不追加整轮流水账；历史实测保留为历史，不再复制到 README。版本取 `Directory.Build.props`，不从旧交接标题猜版本。新功能或发版才能改功能日志，文档整理不伪造新发行版。
- 验证边界：本轮只做源码/链接/Markdown 检查，没有真机安装、卸载或 GUI 点击验证。下方 1.4.9.1 / 1.5.0 的测试记录均是历史记录。


---

## 首批安全与计划修复（2026-10-04，未发布）

- `InstallPaths` 提供规范化目录边界、目录关系校验及统一保护路径。默认 DSH 根下两个独立子目录允许；相等、反向包含、启动器/组件互相包含及侵入 `.dsh`/`plugins` 被拒绝。旧记录卸载不受新布局拒绝限制，所有目录删除目标（启动器、本体、组件、安装器副本、Boot 临时目录）仍应用数据保护，保护目标自身则整项跳过；不扩大删除目标。删除不遍历目录链接，记录目标祖先存在链接时跳过。Node 匹配使用规范化且带分隔符的边界，不匹配同名前缀兄弟目录。
- 修复恢复原范围与三个自定义目录；CLI 只显式覆盖范围/目录和已有开关。开始菜单选项新增可空持久化字段，旧记录回退桌面选项。GUI 修复进入既有确认页，不再直接绕过提权；GUI/静默安装共用有效计划权限原因，拒绝静默提权后立即停止。目录权限改为只读 ACL 估计，不创建探测文件；未知权限保守要求提权，估计不是实际写入保证。
- 扩展既有确认页展示有效目录、组件复用/缺失处理、权限原因、系统改动、数据保留和联网要求；确认按钮先验证目录关系。仅计划展示为只读，未宣称外层 Boot、`--dry-run` 或 `--page` 零副作用。保留此前文案/README/STATUS 编辑。
- 验证：便携 SDK `dotnet build src/DshInstaller/DshInstaller.csproj -c Release --no-restore -p:Platform=x64` 和 Probe 同参数构建均通过（0 警告、0 错误）；Probe DLL `--test-plan-safety` 通过 30 项离线纯路径/状态/权限断言；`git diff --check` 通过。测试入口在系统探测前返回，不联网、不读写真实安装状态、不删文件、不装组件、不改系统、不结束进程。
- 边界：未做 GUI 点击/虚拟机安装卸载/UAC 测试，未打包、提交或推送；ACL 估计可能保守，旧注册表回退不能恢复历史未记录选项，未实现事务回滚或对抗并发目录替换；仍需虚拟机验证旧重叠布局保留数据卸载、机器范围修复及长预览显示。

## 当前源码审查与文案（2026-10-04，历史审查）

- 仅修改 `src/DshInstaller/Localization.cs` 的 15 个既有文案键（其中两个仅改英文）：日志、修复、组件复用、安装前检查、回滚与数据保留说明；纠正用户范围无需管理员、全部组件便携、演练完全不写文件三项过度承诺。保留删除不可恢复提示、全部键与占位符，未修改逻辑，保留原 README/STATUS/HANDOVER 改动。
- 待修逻辑：卸载允许启动器/组件目录与 DSH 根相等或包含它，先整目录删除会绕过 `.dsh`/`plugins` 保留；修复未恢复安装范围且直接进入进度页绕过提权，静默修复覆盖记录的自定义启动器目录；静默提权未检查目标目录写入权限；结束 Node 使用原始路径前缀，可能误匹配同名前缀的兄弟目录。以上为源码确定路径，未执行删除或结束进程来复现。
- 验证：便携 SDK 8.0.425，`dotnet build src/DshInstaller/DshInstaller.csproj -c Release --no-restore -p:Platform=x64` 最终通过（0 警告、0 错误）；135 组中英文键无重复，15 键仅改文案、键顺序/占位符不变；`git diff --check` 通过。系统 dotnet 只有运行时，首次构建无 SDK，改用既有便携 SDK。未运行安装、卸载、启动器或 GUI，未打发布包，未提交/推送。
- 后续优先建立安装路径相等/包含关系校验、统一 GUI/静默/修复/卸载的范围及提权决策；在隔离虚拟机覆盖机器范围修复、保留数据卸载与下载换源完整性。哈希缺失的下载回退和外层 Boot 演练副作用仍需单列验证，不把尚未复现的网络风险描述为已发生漏洞。

## 1.4.9.1 本轮交接（2026-09-23）

本轮已经完成并在本机编译通过：

- 新增推荐插件页并读取 `featured-plugins.json`。
- 推荐插件改为和启动器一致的 GitHub tarball 下载、解压、写入 `link:`、执行 `pnpm install`。
- 插件下载使用 4 线程；Git、Python、pnpm、Node 等其他大文件使用 8 线程。
- GitHub 镜像池统一为 `gh-proxy.com`、`ghproxy.net`、`ghfast.top`。
- 大陆 CDN 与官方下载严格分流，不跨阵营回退。
- 新增欢迎页后的独立下载源页。
- 取消安装后会使用全新的回滚 token，不再因原 token 已取消而跳过所有清理步骤。
- 安装开始前记录三个顶层目录的初始存在状态，避免 Node 先创建父目录导致 DSH 根目录漏记。
- `ProcessRunner` 支持取消令牌，pnpm/git 子进程会随安装取消而结束。
- 卸载检查新增 `ComponentsRoot`，并保留对 `%LOCALAPPDATA%\DeepSeekHarness\Boot` 的清理。

当前状态（2026-09-27）：

- **签名这条路断了**：SignPath Foundation 的免费签名申请**未通过**，官方构建从此走未签名路径，
  README / SIGNING.md / CI 发布说明模板都已按"不签名"改写。别再把"配 SignPath 变量"当发布前置。
- `v1.5.0` 已按**未签名**方式发布（安装器 + 启动器两个仓库的 Release、启动器 npm 包、`manifest.json` 都更新了）。

下一步：

1. 在虚拟机上完整验证 1.5.0：安装、修复安装、卸载保留数据、代理、日志导出。
2. 想签名的话只能走付费服务（Azure Trusted Signing 等，见 SIGNING.md）——那属于另一轮工作。


> **下个大版本硬要求**：安装器改为静态或自包含编译，不再依赖目标机预装
> .NET 8 和 Windows App Runtime；安装器、卸载器与辅助文件放入
> `<DSH 根目录>\dsh`；更新启动器时必须同步发布安装器和卸载器。
> 详细迁移要求见父级 `Dafeiyu-Go\HANDOVER.md` 第六章。

---

## 一、两个项目在哪、什么关系

| 项目 | 路径 | 干什么 |
|------|------|--------|
| **Dafeiyu-Go Setup** | `G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup` | 装机程序。检测环境 → 补运行库 → 装 DSH 本体 → 部署启动器 → 建快捷方式 → 可卸载 |
| **Dafeiyu-Go Launcher** | `G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Click-To-Run` | 托盘启动器(WinUI3),负责跑 DSH 本体、自更新、开机静默启动 |

**关系**:安装器**不把启动器打进包里**(那样每发一次启动器就得重发安装器)。
装的时候去读启动器仓库的 `manifest.json` 拿版本号和校验值,再去下载。

两个仓库:
- https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup
- https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run

> `1.4.9` 过渡期保留旧启动器仓库 `YunxiRamito/DSH-Launcher` 作为下载回退。

---

## 二、本轮(2026-09-19)做完的事

全部由**虚拟机实测反馈**驱动,每条都对应一个用户看得见的现象。

### 下载这条路(本轮改动最大的部分)

**分流规则(用户定的)**:界面上选**加速线路** → 优先国内镜像,GitHub 只当兜底;
选**官方线路** → 全程官方源,一个镜像都不塞(那通常是人在墙外)。

实测数据(2026-09-19,虚拟机,同一个文件):

| 组件 | 加速线路首选 | 对照 |
|------|-------------|------|
| Git/MinGit 46 MB | 华为云 **10,527 KB/s** | github.com 17 KB/s |
| Python 10.8 MB | 华为云 **14,961 KB/s** | python.org 45 KB/s |
| 启动器 10.4 MB | npmmirror **7,365 KB/s**(1.4 秒下完) | gh-proxy 20~126 KB/s(抖) |
| pnpm 20 MB | npmmirror `@pnpm/win-x64` 的 tgz **9,822 KB/s** | github 48 KB/s |
| 运行库 | aka.ms / 微软 CDN(还行,没动) | — |

**为什么启动器最后走了 npm**:Git/Python 有华为云现成镜像,**启动器没有**。
试过两轮:
- **GitHub 加速前缀**(ghproxy 那几家):几十到一百多 KB/s,而且**轮流失效**;
- **jsDelivr**:一开始测出 1.5 MB/s 就上了,结果是**假象** ——
  它只对热门仓库的**已缓存**文件快,我们自己那个冷门仓库走它是回源 GitHub 的速度
  (实测 130 KB/s,八连接甚至 0 字节)。已撤。
- **npm/npmmirror**(现在这条):全量自动镜像,7.3 MB/s,而且**地址由版本号拼出来**,
  以后发版两边都不用改代码。

### 引导程序(Boot.exe)

- 缺运行库的确认弹窗;单根总进度条 + 阶段序号/速度/剩余时间
- WebClient **没有超时** → 加 40 秒无数据看门狗
- `windowsappruntimeinstall.exe` 装完不退出 → 改成轮询"运行库真的好用了吗"
- **TLS 1.2 必须在进程启动第一件事设置**(`ServicePointManager` 是进程级的),
  以前只在下载方法里设,导致"探测分段永远失败"
- 引导程序也走 8 连接分段下载;拿不到 Range 就回落单连接

### 安装向导

- 文案全书面语化;组件页改左右两列(必选在左);页面顺序 DSH 位置 → 组件
- 目录需要管理员权限时当场提示;`NeedsElevation` 也认用户选的目录
- 右键/Alt+F4 关闭会先回滚(`AppWindow.Closing` 订阅以前被误写在拖拽回调里)
- 计划列表只列**这次真要做的事**(快捷方式/PATH/自启没勾就不排)
- 「安装完成后立即启动」以前**根本没实现**,现在点"完成"时按复选框启动
- 开始菜单快捷方式(默认建、可取消、卸载会清)
- 三个 exe 的属性页信息统一从 `Directory.Build.props` 来

### 卸载

- **卸载向导从来没把路径传给步骤**(`session.UninstallOptions` 从来没被创建,
  于是 `new UninstallOptions()` 三个路径全 null,逐个"目录未记录,跳过",
  **一个字节没删**,而跳过只写界面不落盘)—— 这是"卸载像空壳"的真因
- **卸载器删不掉自己**(`DSH-Uninstall.exe` 躺在 DSH 根目录里、自己又在跑):
  现在拉起安装器就退出
- `Directory.Delete(recursive)` 一锤子买卖 → 改**逐项删**,一项失败不再拖累整棵
- 删不干净不再判失败(卸载语义是"能清多少清多少"),只记日志
- 注册表里记 `DshRoot`/`LauncherRoot`/`ComponentsRoot`/`PathEntries`;
  `ConfigStore.Load()` 读不到状态文件就退到注册表

### 下载引擎

- **删掉开下前测速**(分流后候选本身短,测速只是让用户干等)
- 慢下来的处理改成用户要的语义:**后台量一圈别的源,真更快(>1.2 倍)才换**,
  没有更快的就继续用当前这条;换源走 `.part` 续传
- 单连接按速率判(连续 5 秒 < 60 KB/s 触发后台测速);
  分段那条也加了同样的速率判据(注意是**速率**不是"字节数变没变"——
  八条连接慢慢爬时字节数一直在涨)

### 版本与发布

- 版本号收敛到 **`Directory.Build.props` 一处**;`WellKnown.InstallerVersion`
  改成从程序集读(以前两处硬编码,改一处漏一处)
- 安装器 `1.4.9`;GitHub Release `v1.4.9` 已发布，资产为 `DSH-Installer-Setup.exe`
- 安装器仓库加了 `.github/workflows/release.yml` + `RELEASE.md`

---

## 三、待验证(接手第一件事)

本鲸娘(上一个 AI)只做到了"代码路径通 + 单元级的地址/速度实测",
下面这些**没有完整跑通过一次**,需要真机走一遍:

1. **pnpm 从 tgz 解包** —— 只验过 `@pnpm/win-x64` 的 tgz 里确实有 `package/pnpm.exe`
   (`tar -tzf` 看过),**没验过解出来的能不能跑**。
   走的是加速线路时的必经之路。
2. **启动器走 npmmirror 的完整链路** —— 地址、速度都验了,
   但没验过"安装器读清单 → 拼 npm 地址 → 下 tgz → 解出 zip → 校验 → 解压"这条完整流程。
3. **启动器自更新** —— 刚接上 npmmirror(见启动器仓库最近一次提交),
   **一次都没跑过**。升级到旧版本再点"检查更新"就能验。
4. **安装器 `1.4.9` 真机升级回归** —— Release 已发布，仍需覆盖旧安装记录实测。

跑完把 `%LOCALAPPDATA%\DeepSeekHarness\installer.log` 和启动器的 `launcher.log` 拿来看,
里面会写明用了哪个源、多少速度、从哪儿解包。

---

## 四、开发环境与虚拟机

### 编译

```powershell
# 安装器
cd 'G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup'
.\pack-release.ps1                 # 出包并拷到桌面
.\pack-release.ps1 -NoDesktop      # 只出包

# 启动器
cd 'G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Click-To-Run'
.\release.ps1                      # 出包 + 更新清单
.\publish-npm.ps1                  # 发 npm(见下)
```

本机约定:便携 SDK 在 `G:\DeepSeek DSH\.tools\dotnet`,
NuGet 缓存在 `G:\DeepSeek DSH\.nuget-packages`,代理 `127.0.0.1:7890`。
**这些路径只允许出现在脚本里且必须"存在才用"** —— 写死进仓库会让 CI 挂
(启动器的 CI 就是这么连挂八次的,见第五节)。

### 测试虚拟机

- Windows 10 1809(17763),用户 `KitamaruRamito`,`192.168.188.130`
- SSH 助手:`G:\DeepSeek DSH\DSH Works\.fix-lasso-state\vm-ssh.ps1 -Script '<powershell>'`
- 文件下发服务:`vm-server.ps1`(监听 `http://192.168.188.1:8899/`,根目录 `vm-share\`)。
  **它有时会掉**,掉了就重启它,否则推文件会静默失败(哈希打不出来)
- 推送套路:拷到 `vm-share` → VM 上 `Invoke-WebRequest http://192.168.188.1:8899/DSH-Installer-Setup.exe`
  → 比对 SHA256(推之前先杀 `DSH-Installer-Setup`/`DSH-Installer`/`node`/`DeepSeek Harness`)
- **回滚快照会杀掉 sshd**,重跑 `fix-sshd.ps1`(幂等)
- **别点两次安装**:并发会撞车(历史上撞出过 5 个野 node 和一次崩溃)

---

## 五、踩过的坑(改动前先读,能省半天)

1. **本地路径/本机设置写进仓库** → CI 必挂。启动器的 `NuGet.config` 里
   写着 `globalPackagesFolder = G:\...` 和 `http_proxy = 127.0.0.1:7890`,
   CI 上直接报"找不到路径",**连挂八次后工作流被人手动关掉**。
   规则:仓库里只放机器无关的东西,本机设置走环境变量。
2. **jsDelivr 只对热门缓存文件快**。拿别人缓存过的文件测速会得出错误结论。
   冷门仓库走它 = 回源 GitHub。
3. **"只测前三个节点"和"镜像排在最后"凑一起 = 镜像从来没被测到**
   (表现是"配了 jsdmirror 却还走 GitHub")。改动顺序相关的逻辑时,
   一定要把**排序和截断放一起看**。
4. **npm 发布到 npmmirror 有同步延迟**。安装器认的版本号来自 `manifest.json`,
   所以**发版顺序必须是**:发 npm → 等 npmmirror 就绪 → 才更新清单 →
   才发 GitHub Release。`publish-npm.ps1` 里那道闸就是干这个的。
   (安装器侧也做了兜底:探不到就催同步 + 等一会儿再试。)
5. **界面文案与逻辑写在两处会被后执行的覆盖**。卸载完成页显示"安装未完全完成"
   就是 `OnLoaded` 把 `ApplyText` 设好的标题盖掉了。
6. **卸载/回滚的"跳过"只写界面不落盘** → 日志干净得像成功。
   凡是"跳过了什么"都要落盘。
7. **csc 编出来的 exe 默认没有版本资源**。要从程序集特性生成;
   而且"原始文件名"取的是编译时的 `/out` 名字。
8. **`.ps1` 含中文要存 UTF-8 带 BOM**(Windows PowerShell 5.1);
   csc 读 .cs 同理,不给 BOM 就按 ANSI 读,中文变乱码。
9. **Job Object**(`KILL_ON_JOB_CLOSE`)是子进程清理的兜底:
   安装器被中断不会留野 node。
10. 安装器 UI 文案必须是**书面语**,代码注释可以随便说。

---

## 六、npm 通道(启动器发行)

- 包名:`@yunxiramito/dsh-launcher`(**不是** `dsh-launcher`,那个名字被别人占了)
- 版本号 = 启动器版本号;包内容 = 那份 zip
- 安装器/启动器都**由版本号拼地址**:
  `https://registry.npmmirror.com/@yunxiramito/dsh-launcher/-/dsh-launcher-<版本>.tgz`
- 发布:`publish-npm.ps1`(本机);CI 里打 tag 自动发(用仓库 secret `NPM_TOKEN`)
- npm 账号 `yunxiramito` 开了 2FA:命令行发布要么 `-Otp <6位码>`,
  要么用勾了 bypass 2FA 的 token(本机 `.npmrc` 里存了一个;
  npm 正在收紧这类 token,**长期建议改用 Trusted Publishing/OIDC**)

---

## 七、还没做、但可能想做的

- **安装器自己要不要也发 npm** —— 用户说不用,他要传蓝奏云(README 里一句话带过就行)
- **README 补一句国内下载方式**(等用户给蓝奏云链接)
- `docs\文案清单.md` 已过时(文案改过好几轮)
- 安装器仓库的 `payload\launcher.zip` 兜底**没打进正式包**
  (设计决定:断网就装不上启动器,用户明确说不搞)
- 启动器仓库的 `assets\*.zip` 每发一版会多一份 10 MB —— 攒多了要定个清理策略
- jsDelivr 那两条 mirrors 现在还有用(缓存命中的时候 4.7 MB/s),
  但**新版本发布后要等它冷缓存**,所以只能当备胎,别当主力
