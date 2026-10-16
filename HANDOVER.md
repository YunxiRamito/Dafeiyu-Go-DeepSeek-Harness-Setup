# 交接:大肥鱼Go / Dafeiyu-Go

## 2026-10-16 导入文件夹闪退修复（本地候选，未发布）

本轮响应换机用户反馈，安装器保持 1.8.1.0，只交付独立本地修复 EXE；未提交/推送/发布或覆盖原正式包。最终包 ../Temp_SetupandLauncher/Feedback-20261016/import/DSH-Installer-Setup-1.8.1-import-fix-local.exe，11,811,890 字节，SHA-256 7E601226616D7BD422115A0838E78BD45EA5E26E1D8127D8B2BE494B326FF6DC，NotSigned。

- 已实证的根因：导入核心预览哈希回调传入 Double.NaN，原 SetImportProgress 经过 Math.Max/Min 后仍把 NaN 写入 WinUI ProgressBar.Value，异步 Dispatcher 回调绕过预览 try/catch。合成单 session 来源经真实原生 UI 选择后可触发 CoreMessagingXP.dll / 0xc000027b，NaN/Infinity 现转为不定进度、不赋 Value，同一路径不再崩溃。用户故障机/原包尚未复测，不能断言该机器没有其他原因。
- 保留原确认窗口及 Profile/冲突/取消行为，补 picker/确认入口异常日志与可见错误、Shell COM 释放。新增 Setup 专用 ImportSource，不修改双仓共享导入核心：识别 .dsh / 安装根、展开的 home / data/home；独立“从官方迁移 EXE 导入”入口按已有 SFX 协议完整校验并只读解包，绝不执行选中的 EXE。临时来源位于目标安装根旁唯一目录并自动清理，凭据/环境/兼容偏好不导入且明确告知。
- 25 新来源回归、57 既有核心导入回归通过；完整内层 Release 与本机 pack All/Compose 成功。最终包内 68 文件逐项长度/SHA一致、22 XBF/1 PRI齐全、外层附加 ZIP 与 payload.zip 完全一致。Compose 固定“签名 payload”文案不代表签名，本候选确为 NotSigned。
- 最终负载真实 WinUI/UIA 通过 1200/800 宽布局、发现并选择来源、导入确认、空选择与重试、演练结果和关闭后导航恢复。系统 FolderPicker 的直接键鼠自动化受宿主桌面限制未通过，不冒充已验；仍需用户故障机器手动选择文件夹/迁移 EXE 复测。未真实安装/卸载/UAC、写入用户历史数据或打开其全部附件/插件。
- 8 相关源码输入与隔离 BuildSource 哈希一致；导入核心双仓 SHA 均为 B12A714073CD9E303C3CD599675D1B02EEE43E734F8A8D872E843C5D0D75E639。全部 QA/TEMP/cache/产物位于 G 盘 Feedback-20261016/import，自建回归 fixture 已清理，只关闭自建预览进程。

根因前后日志、Windows Event、UIA/截图、构建/打包收据、重建指令与明确边界见该目录 REPORT.md、package-receipt.json、final-source-receipt.json。原 2026-10-14 公开 1.8.1 资产保持不变。

## 2026-10-16 跨仓交接补录：安装器仍为已发布 1.8.1

本节补录 2026-10-14 最终状态，不代表再次发版。Setup **1.8.1** 沿用下方已经校验并发布的 EXE，11,808,313 字节，SHA-256 `35B384F18BE0E35E37AAF69F3BABD47FD58E72974B423E0659824B2DB783E814`，NotSigned；源码 tag `1d56a06314c6b34089a5ae07f53e8a8714ac4772`，Release ID `412659374`。最终本地文件 `../Temp_SetupandLauncher/Delivery-20261014/Installer/DSH-Installer-Setup-1.8.1.exe`。卸载前 DYM / 官方 DSH 自解压还原导出与失败页面导航修正均随该版交付。

启动器纠正“三秒”作用范围后，**1.8.0 也已正式发布**；本节覆盖下方仅描述 Setup 发布任务时“本任务没有发布 Launcher”的跨仓阶段状态。三秒仅作用于主 DSH 窗口标题栏下信息栏，与独立右下角信息窗、安装器和卸载器无关。独立信息窗恢复历史 Information 8 秒规则；未因此修改或重编 Setup 1.8.1。

两仓用户指定日志和本地构建发布记录保留；Setup 发布时完整公开下载校验一致、未由 CI 重建。证据 `../Temp_SetupandLauncher/Publish-20261014/Setup/publication-receipt.json` 与 `REPORT.md`。未真实验证用户安装/卸载/UAC/迁移/自更新，不把发布下载验证当成这些验收。

本次只补交接，不修改功能、不改变 Release/tag/资产、不自动提交推送；历史章节保持原文。


## 2026-10-14 安装器 1.8.1 已发布（本节优先）

按用户明确授权，仅发布 Setup。本地待发布记录已被本节覆盖；复用已验收 EXE，未重新构建或修改字节，未改 Launcher/root/后端/credentials，也未执行真实安装或用户数据迁移。

- Release：`https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.8.1`，id `412659374`；非草稿、非预发布，latest 指向该 Release。
- tag v1.8.1 指向源码提交 `1d56a06314c6b34089a5ae07f53e8a8714ac4772`；隔离 clone selective 提交本轮功能、版本和文档并普通推送，无 force，无覆盖共享 staged/工作区；tag 不移动到后续交接提交。
- 资产 `DSH-Installer-Setup.exe`，id `640227841`，11,808,313 字节；SHA-256 `35B384F18BE0E35E37AAF69F3BABD47FD58E72974B423E0659824B2DB783E814`。本地、API实际下载、公开URL实际下载一致。
- Authenticode **NotSigned**；外层/内部/卸载器版本1.8.1.0。Compose固定文案不能表示SignPath签名。
- `docs/CHANGELOG.md` 1.8.1与GitHub正文使用用户指定原文，标题“安装器 · 2026-10-14 · 本地构建，已发布”，正文exact读回匹配 `Publish-20261014/UPDATE-LOG.md` 的安装器段。
- 创建tag前临时禁用Setup release workflow `361978115`，阻止CI重建覆盖本地资产，结束后恢复原状态active；最终CI查询见 `Setup/ci-verification.json`。未改旧Release/tag/资产。
- 第一次公开读取获得265,464字节HTML，未计成功；随后同一公开URL及带cacheBust读取均取得正确EXE。未重复上传、删除或覆盖资产，不能断言具体缓存故障源。
- 原198输入验收记录保留；发布clone核对196非文档输入内容一致，Git换行差异明确记录，CHANGELOG/HANDOVER为允许文档例外。payload68、22XBF/1PRI及各专项沿用验收结果，未重跑。本轮未真实安装/卸载/UAC/自更新/迁移，插件依赖与历史附件完整可用性仍有实际环境边界。
- Launcher安装器更新通过Setup latest Release自动读取固定资产名，不需要同步另一个manifest；本任务没有发布Launcher。

完整发布收据、API/公开下载、源码提交清单及exact正文在 `../Temp_SetupandLauncher/Publish-20261014/Setup/REPORT.md` 与同目录json/md。所有本轮发布临时文件在G盘。

## 2026-10-14 安装器 1.8.1 本地交付（本节优先）

最终状态：本地交付已冻结，待发布。主 Agent 已独立核验二进制并汇总主报告；本任务最后仅保存文档，没有扩大验证或修改功能。

用户要求将安装器从 1.8.0 升至 1.8.1 并交付 EXE，不发布。本任务仅改版本文件、Setup 更新日志与本交接文档；保留工作区原有官方导出/还原及卸载改动，未编辑 Launcher 源码，未提交、推送、改公开 manifest 或使用 CI。

- 最终 EXE：`../Temp_SetupandLauncher/Delivery-20261014/Installer/DSH-Installer-Setup-1.8.1.exe`，11,808,313 字节；SHA-256 `35B384F18BE0E35E37AAF69F3BABD47FD58E72974B423E0659824B2DB783E814`。外层、内部安装器、共享库及独立/嵌入卸载器文件与程序集版本均为 1.8.1.0，产品版本 1.8.1。
- **最终 Authenticode 为 NotSigned。** Compose 日志固定写“使用 SignPath 已签名 payload”，本轮未签名、未执行 SignPath、未启用 RequireSignedPayload，勿依据该文案宣称已签名。
- 相对 tag v1.8.0 的新增主要为卸载前 DYM / 官方迁移 EXE 导出、独立官方还原工具与导出/卸载导航门槛；Runtime 检测修复已在 1.8.0，未重复写为新增。源码日志 `docs/CHANGELOG.md`，供主 Agent 合并的 `Installer-CHANGELOG.md` 在交付目录。
- 在交付目录 BuildSource 快照用现有 `pack-release.ps1 -NoDesktop -OutputDirectory` 本机构建，所有本任务缓存/临时/输出在 G 盘。初次 MSBuild powershell.exe 9009 已通过精简 PATH 解决。原 All 阶段删除 RID 目录时丢失 XBF，最终从本轮输出补齐全部22 XBF/1 PRI，再用 Compose 重组；仓库打包脚本未改。
- 68 个 payload 文件及最终附加 ZIP 逐项大小/SHA-256、资源与版本一致；只读 PE manifest 确认外层 requireAdministrator / 内部 asInvoker。198 个构建输入在最终 UI 后与收据一致；随后仅追加本交接文档。
- 七组已有核心专项 274 项与最终负载中英文 UIA 各31项，共336项通过。核心回归使用最终共享 DLL 或生产导入源码；七组构建0 warnings / 0 errors。导入/发现双仓一致通过。UI仅操作自己创建的隔离进程，两轮fixture已清理，应用进程退出；没有关闭其他日常窗口。
- 未真实安装/卸载/UAC/运行库安装/自更新/迁移，未在真实官方 DSH 验证全部历史附件与插件；部分插件需官方命令重建依赖。安装器仍按需联网获取运行库/启动器，不内嵌本轮本地 Launcher，不代表全离线安装。未补跑本轮独立 WPF 还原 UI，历史证据不可代替。

完整报告、签名说明、复现脚本、源码/负载收据、日志与截图均在 `../Temp_SetupandLauncher/Delivery-20261014/Installer/REPORT.md` 及同目录。主 Agent 可直接汇总该目录产物；按用户最后要求不扩大验证。

## 2026-10-14 启动器窗口与导出联动验收

启动器 1.8.0 仍未发布。本仓安装器的官方 DSH 导出、DYM 导出和卸载流程已用 `Installer-Final2` 做中英文 UIA 验收；官方还原自解压窗口保持无 WinUI 3 依赖的独立实现，文件夹按钮使用 Shell 图标。安装器候选和证据由启动器交接文档中的 `WindowAndExport-20261013` 目录记录。

- 官方导出核心 37/37、中文/英文导出 UI 各 31/31 通过；官方 desktop profile 使用真实确认的 `@deepseek-ai/dsh-base` + `@deepseek-ai/dsh-web-app`，保留已有 manifest bundles。
- 本轮未修改真实 `G:\DSH_Official`、用户数据、凭据或注册表；所有 fixture 和临时输出位于上级 `Temp_SetupandLauncher`。

## 2026-10-13 安装器 1.8.0 官方版迁移最终交付（优先于下方历史）

本轮安装器已正式发布 GitHub v1.8.0；启动器仅本地编译，网页和手机端不在本轮范围。用户指定 Release 正文保持单行：`修复检测不到安装组件的潜在bug`。

- Release：`https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.8.0`，id `411283070`，tag 指向 `2385da7a3b095a77c7b4565aee3b51f137104e04`。资产 id `635914389`，GitHub 返回的大小及 SHA-256 与下方本地包一致；非草稿、非预发布并设为 latest。tag CI `38347936878` 已确认 completed/cancelled，避免覆盖本地验收资产。

- 最终包：`../Temp_SetupandLauncher/OfficialMigration-20261013/Installer-Release180/DSH-Installer-Setup.exe`，11,656,818 字节；SHA-256 `ADC2AAFAC2A420726158533D108A67C26286096CE01F4F55EE49F0B6C4164E3C`。Boot、内部安装器和卸载器均为 1.8.0.0；打包 7/7 与内置自检通过。
- Runtime 检测独立读取 HKLM/HKCU，接受合法的 Main/framework 1.8 包及正确架构，取最高版本；错误 `0x8007177E` 的 EFS 策略诊断保留。未在反馈用户的真实电脑复现或修改其策略。
- 官方版按 HKCU/HKLM、32/64 位卸载注册表定位，接受名称附带语义版本，联合验证 `resources/app.asar` 与 `resources/runtime/cli/bin/dsh.cmd`。没有写死目录名，排除同名 Chrome PWA。真实注册表及无 hint 查找已识别本机官方安装。
- 多个 DSH 明确列出供用户选择。官方版只作为迁移来源，不能直接复用；用户选择“迁移官方版对话、插件和 Skill”或“全新安装 DSH”，均使用独立 npm 安装目录。来源默认 DSH_HOME/用户 .dsh，可展开更改；提权计划保留原用户来源和设置路径。
- 安装后自动预览迁移并明确确认，desktop profile 默认迁至 web；未导入时须明确勾选跳过才能结束。会话包含 storages/attachments；保留目标同名数据并跳过冲突，保留官方安装及源数据。拒绝来源/目标嵌套、重解析路径及运行中的官方源；只迁移允许的数据，不迁移根凭据、环境配置或 desktop app bundle。无法携带的本地插件依赖会明确警告。
- 兼容在线稳定启动器 1.7.5：成功安装后备份原用户 LauncherSettings.json，再更新本次明确选择的 DSH/Node 路径，保留其他偏好与凭据；切换本体只清除旧 DSH API key 缓存。启动器运行中不强写设置，而是在完成页提示退出并重新选择。新本地启动器另支持 schema2 一次性来源选择及正确 Skill/插件根目录。
- 安装器 ICO 和窗口 logo 使用用户提供的第二张图；启动器保持原图案压缩。InstallerLogo 14,215B；安装器 ICO 73,779B；启动器 ICO 59,483B、Info ICO 22,169B、PNG 148,387B（原 5,088,686B）。
- 验收：检测/Runtime 87 项、迁移与安装相关 352 项、稳定启动器选择 13 项、本地启动器来源/Node 62 项、插件/Skill 115 项、Windows 原生图标 27 帧通过。最终真实 WinUI/UIA 7 项及两张截图通过，见 `../Temp_SetupandLauncher/OfficialMigration-20261013/UiSmokeFinal5/`；验收脚本已等待 UI 异步布局和有效窗口尺寸。导入/发现双仓源码一致门禁通过。
- 未执行真实用户数据迁移、真实替代用户 UAC、物理 Windows10 或故障用户机器复测；文件/附件测试不等于在 DSH 内实际打开全部历史附件和第三方插件。

综合报告：`../Temp_SetupandLauncher/OfficialMigration-20261013/REPORT.md`。只发布本安装器；启动器含其他任务未发布工作，不能直接将其整个工作区提交发布。

## 2026-10-13 浏览器安装后 Runtime 识别修复

用户反馈“已经通过浏览器安装 Windows App Runtime，但安装器仍显示缺少”。此前探测只读取：

- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications`
- `MicrosoftCorporationII.WinAppRuntime.Main.1.8_` 包名前缀

这会漏掉按当前用户注册的 AppX 包，以及使用 `Microsoft.WindowsAppRuntime.1.8_` 前缀的合法 1.8 包。现在 `boot/Boot.cs` 与 `src/DshInstaller.Shared/Detection/EnvironmentProbe.cs` 统一检查全局 AllUserStore 和当前用户 Package Repository，接受两种合法包前缀，解析全部版本并取最高版本；只接受带有合法四段版本号的主 Runtime 包，不会把无关包或 DDLM 包当成 Runtime。

启动器初始日志和运行库安装失败日志还会记录实际匹配到的包名，便于区分“浏览器只下载了 EXE”“AppX 没有注册”“探测逻辑漏包”和“EFS 策略阻止安装”。

v3 隔离构建结果：

- EXE：`../Temp_SetupandLauncher/Installer-EFS-Fix-20261013-v3/DSH-Installer-Setup.exe`
- SHA-256：`A512A096BEE3DD4B60EE5F27E4057ABEBC35F988357C239226AA5D5DDB390BD8`
- `pack-release.ps1 -NoDesktop`：7/7 步骤成功，自检通过
- `DshInstaller.Shared`：构建成功，0 警告、0 错误
- Probe：当前用户与全局包均可识别，报告 `8000.994.2142.0`，必需组件全部就绪
- 当前未发布、未上传 Release

## 2026-10-13 Windows App Runtime 安装失败诊断与本地修复

其他用户反馈引导日志反复出现：

```text
Windows App Runtime 退出码 = -2147018882
Windows App Runtime 装完还是不达标: .NET=8.0.31 / WinAppRuntime=(缺)
```

`-2147018882` 等于 `0x8007177E`，Windows 错误为 `ERROR_VOLUME_NOT_SUPPORT_EFS (6014)`，含义是安装过程使用的工作目录或暂存卷不支持文件加密。日志显示运行库下载已经完成，失败发生在 `windowsappruntimeinstall-x64.exe --quiet` 执行阶段，不是下载校验失败。

本地修复位于 `boot/Boot.cs`：运行库安装进程现在显式使用下载文件所在的 `%TEMP%` 目录作为 `WorkingDirectory`，不再继承用户启动 Setup 时的当前目录；同时记录当前目录、TEMP 和安装包所在卷的文件系统及剩余空间。遇到 `0x8007177E` 时会给出针对“不支持 EFS 的 U 盘、网络盘、虚拟盘或系统策略”的提示，而不是让用户无信息地重复安装。

隔离构建结果：

- EXE：`../Temp_SetupandLauncher/Installer-EFS-Fix-20261013-v1/DSH-Installer-Setup.exe`
- SHA-256：`11F084BD79A9BFCFF3A620B3AA3304DD28772F0E9D23B60AF0072256A7F6FA20`
- 构建步骤 1/7 至 7/7 成功；当前未发布、未上传 Release。

如果该用户使用修复版仍失败，需要从最新 `%TEMP%\dsh-boot.log` 提供新增的“Windows App Runtime 安装环境”一行，并在其机器执行 `fsutil fsinfo volumeinfo C:`、`fsutil behavior query disableencryption`，确认系统盘/TEMP 所在卷和 EFS 策略。不要先修改注册表或强行关闭安全策略。

## 2026-10-10 12:40 v1.7.3 已发布（本节优先）

- Release：`https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.7.3`（id `408645721`，tag v1.7.3 → `07e0555`）。
- 资产：`DSH-Installer-Setup.exe`，11,476,468 字节，SHA-256 `8510DE228A5F601AD77FB402C79CAB765088ECA750E500D9EAB57DE29FE803D3`，FileVersion `1.7.3.0`（本地 `pack-release.ps1 -NoDesktop` 构建）。
- 版本号改动：`Directory.Build.props`（InstallerVersion `1.7.3` / InstallerVersionFull `1.7.3.0` / LauncherVersion `1.7.3`）与 `src/DshInstaller/app.manifest`（`1.7.3.0`）。原因：用户附带的 EXE 版本资源仍是 `1.7.2.0`，而本次发布 tag 是 v1.7.3；启动器按「Release 版本 vs 本机记录的安装器版本」判断更新，版本资源对不上会让新装用户被反复提示安装器更新。用户选择升版本号重编。
- 更新日志新增 `## [1.7.3] - 2026-10-10`（用户给的合并文案）；`.gitignore` 补了 `dist-*/` 与 `.build-temp/`。
- tag CI run `38024496442` 已取消，避免 CI 产物覆盖 Release 资产。

## 2026-10-09 14:45 v1.7.2 已发布（本节优先）

- Release：`https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.7.2`（id `407608613`，tag v1.7.2 → `f521582`）。
- 资产：`DSH-Installer-Setup.exe`，11,474,701 字节（文件版本 1.7.2.0），SHA-256 `AEF9BF3DA0E2DB32A4F28A0DBD2C270C16352590C3DD5AC822C18D74B8E693F7`（用户提供的本地构建）。
- `.gitignore` 补了 `dist-*/`，避免 60 MB 级构建目录进仓库；tag CI run `37895223168` 已取消。

## 2026-10-09 00:10 v1.7.1 已发布（本节优先）

- Release：`https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.7.1`（id `407026440`，tag v1.7.1 → `2cc0c0d`）。
- 资产：`DSH-Installer-Setup.exe`（用户提供的 `DSH-Installer-Setup-1.7.1.exe` 按更新检查期望的文件名上传），11,486,851 字节，SHA-256 `F6434A95459F0FFBBB6F4C9F7FE6B724E8805012E6EE9C12AA0E49D53723FAA2`。
- tag CI run `37806535655` 已取消，避免 CI 产物覆盖 Release 资产。

## 2026-10-08 19:40 v1.7.0 安装器已发布（本节优先）

- tag `v1.7.0`（→ `f300901`）CI run `37765095255` 成功，Release：`https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/tag/v1.7.0`（id `406714406`）。
- 资产：`DSH-Installer-Setup.exe`，11,441,550 字节，SHA-256 `adbeda9ab28a743b3979401713748452ea4d3a9fc43b8f7f866be68568915666`（CI 构建，与本机 9FDDB426… 的不同属正常）。
- 更新日志 1.7.0 已改为「已发布」并推送（bc38e8b）；Release 正文写入同一份合并文案。
- 启动器侧 1.7.0 为本地发布（见父级 HANDOVER）：ZIP 哈希 `736A28A9…`。

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

---

## 2026-10-16 补充：正式打包 XAML 资源根目录修复

- 根因：旧 `pack-release.ps1` 从 `bin/Release/<TFM>` 递归取 XBF/PRI，实际构建产物在 `win-x64` 内，复制后落成 `payload/win-x64/...`；数量检查先通过，随后清理 `win-x64` 又把全部资源删掉。仅检查 EXE 版本无法发现。
- 修复：只从本次发布的 `bin/Release/<项目 TargetFramework>/win-x64` 取资源，保持相对于 RID 根的 `App.xbf`、`Pages/*.xbf`、`Controls/*.xbf`、`Styles/Theme.xbf` 路径；缺失时失败，不回退旧产物。框架与 PRI 名从项目文件读取，XBF 清单从实际 XAML 源码生成，新增页面自动成为必需资源，不依赖版本号或数量阈值。当前为 22 XBF + `DSH-Installer.pri`。
- `All`、`PrepareInner`、`Compose` 均逐项校验文件存在且非空，禁止 payload 根残留 `win-x64`；打 ZIP 后再次校验精确 entry、重复/缺失/空文件；最终自解压 EXE 中的整段 payload 长度及 SHA256 必须与验收 ZIP 相同。
- 本地回归 `tools/PackResources.Regression.ps1` 使用生产脚本 AST 提取的实际函数，12 项通过：真实隔离 RID 拷贝、30 个诱饵 XBF 仍无法掩盖缺页、空 App、缺 PRI、残留 RID、缺源先失败、新增页面、ZIP 正常/重复/缺页/残留 RID。产物位于项目 G 盘 `Temp_SetupandLauncher/Feedback-20261016/pack-resources/`。
- 实际 `-Stage Compose -NoDesktop` 使用此前隔离 BuildSource 的 payload，仅生成新的 QA 包，无整仓编译、提交、推送或发布。`compose-verified/resource-receipt.json`：最终 EXE 提取 payload 共 68 entries，22 XBF、1 PRI，App/ImportPage 各 1，无 win-x64，提取 SHA256 与源 ZIP 相同。此 QA 包沿用现成 1.8.1 payload，不应当作新正式发行。旧 import-fix 候选 EXE 的 SHA256 仍为 `7E601226616D7BD422115A0838E78BD45EA5E26E1D8127D8B2BE494B326FF6DC`，未覆盖。
- 改前脚本与 HANDOVER 备份：`Temp_SetupandLauncher/Feedback-20261016/pack-resources/backup/`。发布负责人必须在修后脚本已冻结后再创建 source snapshot；正式整包验收由 release owner 执行。
