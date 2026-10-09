# 变更记录

本文件按 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 维护：一个版本一节、最新在上，
日期用 `YYYY-MM-DD`，改动按 Added / Changed / Fixed / Security 分类。
写给用户看：说人话，只讲用户在乎的事，不写内部实现。
安装器和 DSH 启动器同步发版，所以同一节里两边的改动都写。

**写日志的风格（重要，别写成说明书）**

- 不提类名、接口、字段、提交号、哈希、测试套件名这些内部东西；只说「用户能看见什么变了」「以前怎样、现在怎样」。
- 俏皮一点、有情绪，像跟网友聊天：可以口语、感叹号、自嘲和吐槽，允许括号里的碎碎念，
  例如「应该不会再报错了吧（？）」「这可能需要消耗一点点 Token，但是…有总比没有强！」。
- 大更新各占一条，开头一句「报告！大大大大大的来了！」这类开场白，再逐条讲这次改了什么、对用户有什么用。
- 感知不强的并成一行罗列：用「其余小改动：A；B；C。」一句话带过；修复用一句话说清前后差别。
- 标点断句随意，品牌名与专有名词写规范：GitHub / DSH / Token / Markdown。1.6.0 那一节就是标杆。

## [1.7.2] - 2026-10-09

**启动器 + 安装器 · 2026-10-09 · 已发布**

每次写更新日志都很苦恼，不知道该怎么写，今天看了看国民级App的更新日志后也灵感乍现了。

本次更新:
- 解决了一些已知问题。


才怪，我只是想告诉你，这次主要是修复功能为主的更新。
上个版本的更新完善了备份导入导出，但是我测试时发现会报错，于是————

### 修复

- .dym备份文件和原版dsh备份的导入与导出；
- 修复技能页雷霆大加载的特性；
- 还有一些其他问题

## [1.7.1] - 2026-10-08

**启动器 + 安装器 · 2026-10-08 · 已发布**

报告！1.7.1 先把 DSH 服务稳稳开起来，再去料理其他更新喵！这次是官方修复版，走启动器自更新，不需要高危补丁确认喵！

### 启动顺序

- 启动时优先完成 DSH 更新并启动服务，等服务就绪后再检查启动器、安装器、在线插件和补丁，不让一串更新挡住你打开 DSH 喵！
- 安装器的更新检查修好了，现在和启动器、DSH、在线插件一样有「自动下载并安装 / 仅检查更新 / 关闭」三个选项喵！

### 数据迁移

- 「通用-数据备份」搬到「关于 → 备份与导入」了，原来的 DYM 备份照常能用，还把 Skill、插件和配置一起带上喵！
- 现在可以直接导入官方 DeepSeek Harness 文件夹里的会话、Skill 和插件，也能导出解压后直接覆盖官方数据目录的 ZIP 喵！安装器的导入页也支持这一套数据喵！
- 导入、导出按钮点下去就打开 Windows 文件或文件夹选择器，选完再确认要搬哪些内容，不用先填一堆路径喵！
- 忘了旧数据放在哪？启动器和安装器都会先查桌面、文档、下载和常见 DSH 目录，也能扫描其他固定磁盘；找到多个 DYM 或 DSH 来源时，会把完整路径列出来让你自己选喵！

### 通知与提醒

- 以前走 Windows 的提醒也统一进右下角信息窗，插件更新、后端状态和反馈提醒都能在这里看；「通用 → 提醒」顶部新增通知静音，关掉提示音也会继续显示通知喵！
- 提醒页重新排版，边框和其他设置页统一；今日消费和余额提醒并排放，自定义金额输入框紧跟在选项右边喵！
- 开发者身份收到新的反馈与建议时，会在信息窗里收到提醒喵！

### 优化与修复

- 反馈图片在方形预览中会居中显示了；插件内容改成按需加载，图片预览限制解码尺寸，少占一点运行内存喵！
- 信息窗和启动器共用已有 Windows App Runtime，清掉包里重复塞进来的运行库，1.7.1 启动器压缩包比 1.7.0 小很多喵！
- 修复部分客户端收到通知却没有展示，以及安装器检查不到更新的问题喵！

## [1.7.0] - 2026-10-08

**启动器 + 安装器 · 2026-10-08 · 已发布**

报报报报报报报报告！1.7.0大的又来了喵！！本鲸娘狂烧500大洋搞了超大的更新，还不来捐捐款，真的要吃土了喵......

### 后端接入

- 启动器现已接入后端了喵！本鲸鲸有房子住了喵！欢迎来屋子里获取公告与通知，还可以穿过屋子去GitHub代码库逛街喵
- 服务器掉线和恢复时会通知你的喵！不要趁着找不着大豪斯就背地里偷偷干坏事喵
- 如果翻代码翻插件翻不到了，也可以让我帮你找喵，下载源接入后端了喵

### 新增

#### 关于-反馈与建议
- 可以提交问题、提出建议、追加补充，查看自己的提交和处理进度喵！反馈和补充可以附图片：每次最多 5 张、每张不超过 5 MB，支持 JPG、PNG、WebP（不要传一些奇奇怪怪的东西喵，本鲸鲸看到奇奇怪怪手会不小心点到封禁喵）；支持上传启动器日志，方便本鲸更快的定位问题喵

#### 左下角-在线人数-启动器面板
- 支持来我的大豪斯看看，还能看到多少人在做客喵！豪斯可是我花了血本租下的，不许在我的大豪斯乱涂乱画喵

#### 更新-补丁
- 启动器的小改动本鲸懒得推更新了！赏一个补丁给你（︶^︶），这些更新部分会留到大版本合并进代码喵

### 优化

- 启动器日志变得更详细了喵！一切都是为了想要做好启动器，才、才不是为了你呢(#`O′)
- 右下角的信息窗大改版喵！本鲸特地加了提醒音效和丝滑的动画，之后致力把所有提醒都塞进去喵

### 修复

- 修复Windows资源管理器挂掉后托盘被Windows娘吃了的特性喵
- 修复了部分插件无法更新的问题喵
- 部分插件太旧了现在会提醒你不兼容喵
- 修复插件下载遇到临时超时后直接失败的问题喵
- 本鲸娘狠狠的敲打了一下Powershell，优化了一下安装流程喵
- 好饿啊，想去吃辣椒炒肉拌饭喵...

## [1.6.0] - 2026-10-07

**安装器 + 启动器 · 2026-10-07 · 已发布**（安装器与启动器最后一次同版发布，之后各自独立更新）

报告！大大大大大的来了！

- 我们重新设计了设置界面，导航收成六项，标题和页签统一了字号与间距，主页卡片按常用程度重排；
- 第二个大更新则是顶部的搜索框，现在你已经可以搜索设置项了，甚至首字母搜索 SZMSS 也是支持的；
- 插件现在粘贴 GitHub 仓库链接也可安装了，优先走新的 DSH 官方插件入口，应该不会再报错了吧（？）；
- 现在已支持把技能和插件的英文用大肥鱼直接翻译成中文（这可能需要消耗一点点 Token，但是…有总比没有强！）；
- 安装器与启动器独立更新：两边各自读自己的版本，不再因为版本号不同而卡住启动器更新，「更新」页新增「修复安装器」。

其余小改动：更新小窗去掉白边改成整窗滑入滑出；图表纵轴改用普通数字；关于页版本合并到一处；插件自检/一键修复移到列表下方，不再挡住安装入口。

修复：更新按钮修好，检查中防重复点击、失败给中文原因和「重试」，没有来源的手动插件不再假装「已是最新」；也修好了带特殊头部的合法 tar.gz 被误判成「含不支持的链接」而装不上的问题。


## [1.5.4] - 2026-10-06

**安装器 + 启动器 · 2026-10-06 · 已发布**

### 余额与 Token 图表

- 改进余额、今日 Token 与估算花费摘要，保留近 7 / 15 / 30 天趋势和悬停明细。
- Y 轴使用短单位刻度，减少长数字占用绘图区；完整数值仍可在悬停明细查看。
- 修正余额观测的账户、币种及日期口径，避免切换密钥或币种被误记为消费。余额变化是观测差额，不是完整账单；Token 花费仍按模型单价估算。

### 下载中心

- 侧栏左下角增加下载任务入口，管理入口显示时自动上移；显示进度、速度、状态与历史。
- 当前下载支持暂停 / 继续 / 重试 / 取消；重试在原任务仍存活时继续，历史记录不会重新执行过期的安装操作。
- 加强分片长度和范围校验，取消后停止备用源尝试；各更新任务使用独立暂存目录。

### 代理设置

- 增加两个独立生效开关：启动器默认开启，DSH 服务默认关闭；旧配置沿用此默认值。
- 启动器范围覆盖自身联网请求及其包管理子进程；DSH 范围在下次启动服务时生效，不会自动重启当前服务。
- DSH 关闭时不接管其环境；开启时按所选模式配置。DSH 代理支持受其网络层能力限制，不把 SOCKS5 的静默直连作为成功。

### 安装器与验证

- 安装器同步为 1.5.4，用于配套部署和启动器更新版本匹配；本轮未新增安装 / 卸载流程功能。
- 独立审查和修复完成：专项回归 32 + 32 + 34、安全/完整性 61 + 11、安装器计划 30 项通过，真实 DSH 本地代理集成通过，两仓正式 CI 成功。
- 隔离代理范围 UI 点击、保存与重载通过；下载入口和空任务页可见。图表视觉/DPI、完整下载按钮 GUI 操作、UAC、完整安装卸载 e2e 未测，符号链接权限用例跳过。
- 两个 Release 更新日志已读回核验；npm 与 npmmirror 下载一致且包内 ZIP 与正式资产一致。两包未签名；清单提交 d8b6bd6，raw 与 jsDelivr 公开清单均核验为 1.5.4。正式哈希与 CI 记录见 HANDOVER。

## [1.5.3] - 2026-10-04

**安装器 + 启动器 · 2026-10-04 · 已发布**

### 新增

- 关于页增加只读健康预览和白名单 JSON 导出，只展示版本、架构、DSH/Node 可用性、端口状态与建议，不包含原始日志、密钥或完整自动修复。
- 安装计划会说明安装目录、可复用组件、系统改动、数据保留、联网要求和权限提示；权限预览只是估计，不保证实际写入结果。

### 修复与改进

- 技能和插件的 tar.gz 解压会拒绝越界路径、绝对路径、链接、保留设备名、数据流和目录逃逸，并先校验再替换；ZIP 解压流程未在本版重写。
- 卸载保留数据保护覆盖全部删除目标及旧的重叠目录布局；修复会恢复原安装范围和自定义目录，命令行只有明确指定才覆盖，图形界面和静默流程统一权限判断。
- Node 进程只匹配完整目录前缀，避免误伤同名前缀目录；更新包缺少或校验值无效时停止更新。
- 精简界面和文档，明确安装计划、权限、联网、隐私和演练限制。
- 清理 README 的历史记录和过时的 1.3.21 归档宣传内容。

### 验证与边界

- 离线回归 61 + 11 + 30 项通过，Release 构建通过；GUI、UAC、完整安装/卸载流程未测，符号链接用例因 Windows 权限不足跳过。
- 本版只覆盖明确列出的边界，不宣称所有安全问题已解决，也不宣称具备完整事务回滚。
- 安装器 CI `37179657388`（commit `abf3a0a`）产物 SHA256：`9e770e068c997fec62956ce362250cba0cfd12d64491f96b03c1f943e7f9b88`；启动器 CI `37179772669`（commit `950d354`）产物 SHA256：`56a21d1057a392a4b5b24c3f2d74aa34fc98d529242dc02a188a5332045c66ec`。npm 1.5.3 与镜像均 HTTP 200，下载包 SHA1 `d0af60ab658f15ecc216b02cfc7408be30e5d6747b478fdb1d57d87f8d4a9f5`，npm 与镜像 tgz 一致且内含 ZIP 与官方哈希一致。

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
