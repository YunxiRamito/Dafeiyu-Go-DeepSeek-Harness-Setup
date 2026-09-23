# 大肥鱼Go / Dafeiyu-Go Setup — 项目状态

> 这份文档是给"下一个我"看的。每次开工先读这里,收工前更新这里。
> 最后更新:2026-09-23(1.4.9.1 安装器、回滚与下载源改造)

## 1.4.9.1 当前状态

- 推荐插件页、下载源页和平台风格界面已完成。
- 推荐插件不再走 Git SSH，改为 GitHub tarball 镜像下载，4 线程、解压、写 `link:`、pnpm install。
- 插件与组件共用 GitHub 镜像池：`gh-proxy.com`、`ghproxy.net`、`ghfast.top`。
- 大陆 CDN 与官方下载严格分流。
- 取消回滚已修复两个根因：回滚使用独立 token；安装开始前记录顶层目录。
- `C:\Program Files\DeepSeek Harness` 残留问题已在虚拟机复现并定位，代码已修复。
- 当前唯一发布阻塞是 SignPath 仓库变量和 secret 未配置。

---

## 一句话

为大肥鱼Go做一体化的 WinUI3 安装程序，底层面向 **DeepSeek Harness(DSH)**：检测环境 → 缺什么补什么 → 装 DSH 本体 → 装启动器 → 建快捷方式 → 可卸载。中英双语，只支持 Windows 10 1809 (build 17763) 及以上。

---

## 我在哪

```
G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup
```

配套项目(DSH 启动器,本安装器负责把它铺出去):

```
G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Click-To-Run
G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Click-To-Run\source\dist-1.4.9
```

DSH 本体(被装的宿主):

```
G:\DeepSeek DSH                    (node_modules\@deepseek-ai\dsh)
G:\DeepSeek DSH\DeepSeek Harness   (启动器部署目标)
```

---

## 已拍板的决策(别再问第二遍)

| # | 事项 | 决定 |
|---|------|------|
| 1 | 组件安装策略 | **全便携**:解压到自选目录,不写系统,卸了就删目录 |
| 2 | Node 装法 | **全局装**(唯一动系统的组件);已有 ≥22.13 就不动它 |
| 3 | 离线包 | **不做**,只在线 |
| 4 | 界面语言 | **中英双语**,跟随系统语言,右上角可切 |
| 5 | 系统门槛 | **低于 1809 直接不支持**,停在欢迎页给明确提示,不做兜底 |
| 6 | 提权设计 | **一个一次性提权 worker**,全程只弹一次 UAC |
| 7 | 卸载程序 | **独立 exe**,卸载时询问"要不要一起卸载 Node" |
| 8 | 仓库 | 安装器 `Dafeiyu-Go-DeepSeek-Harness-Setup`;启动器**单独一个库**(见下);都 MIT |
| 9 | 代码签名 | 暂不签,文档里写清楚 SmartScreen 怎么过 |
| 10 | 测试环境 | 用户有虚拟机,可放开手测;(开发期用 `--dry-run` 在本机演练) |
| 11 | 可选组件 | Node 之外(pnpm / Git / Python)用户勾了也是**便携版** |
| 12 | 启动器分发 | **不打包进安装器**,改成读启动器仓库的 `manifest.json` 现下最新版 |

**关于签名**:免费方案已确认——自签名对陌生用户无效;SignPath.io 对开源项目免费(要申请,可签 exe);Azure Trusted Signing 约 $10/月。结论:先不签,README 里写清楚。

---

## 两个仓库怎么配合(重要)

```
deepseek-harness-launcher          ← 启动器(现在的 DeepSeek Starter 项目)
  ├─ manifest.json                 发布清单:版本 + zip 地址 + sha256
  ├─ RELEASE.md                    发版流程(已写好)
  └─ Releases/DeepSeekHarness-x.y.z.zip

dsh-installer                      ← 安装器(本项目)
  └─ 装的时候:LauncherFeed.Fetch() 读清单 → 下 zip → 解开
```

要点:
- 启动器发新版 → **只推启动器库**,安装器不用动,新装用户自动拿到最新
- 清单读不到(断网/仓库没建好) → 回落到安装器自带的 `payload\launcher.zip`
- 国内加速已内置:manifest 走 jsDelivr,zip 走 `ghproxy.net` / `ghfast.top`
- 清单格式与镜像策略见 `docs\ARCHITECTURE.md`,发版步骤见启动器库的 `RELEASE.md`

**已经接上了**:

| 项 | 值 |
|----|-----|
| 启动器仓库 | `YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`；旧仓库作为 1.4.9 回退 |
| 首批版本 | `1.3.9`,zip 10.4MB,sha256 `6c8fb51c…41f0` |
| 清单文件 | 启动器仓库根目录 `manifest.json`(已生成,待推送) |
| 一键发版 | 启动器项目里的 `release.ps1`(编译+打包+算哈希+写清单) |
| CI | 启动器项目 `.github\workflows\release.yml`,打 tag 自动出包 |
| 安装器侧的 git | 启动器目录已 `git init` 并配好 `origin`(还没 commit/push) |

---

## 关键事实(踩过的坑,别再踩)

1. **DSH 的 node 是 npm 包**。用户的 `package.json` 里 `dependencies` 同时有
   `@deepseek-ai/dsh@^0.1.5-rc.1` 和 `node@^24.21.0`。所以 npm 装 DSH 时会顺带把 node 拉进
   `node_modules`。但**全局 Node 仍然必须**,因为 `npm install` 本身要 node/npm。
2. **DSH npm 包已发布**:`https://www.npmjs.com/package/@deepseek-ai/dsh`。
   `registry.npmjs.org` 和 `registry.npmmirror.com` 都可达(实测 200)。
3. **最低系统**:Windows App Runtime 1.8 要求 Win10 1809 (17763)。
   1803 (17134) 起不来。DSH 本体本身没有硬性系统要求(纯 Node 服务)。
4. **启动器是 WinUI3 未打包应用**,靠 `Microsoft.WindowsAppRuntime.Bootstrap.dll` 初始化,
   所以安装器自己**也**需要 Windows App Runtime 1.8 —— 先有鸡还是先有蛋的问题,
   靠 `DSH-Installer.Boot.exe`(.NET Framework 编译)解决:检查 → 缺则下载安装 → 再拉主程序。
5. **提权进程的 PATH 陷阱**:安装器提权后 `Environment PATH` 是管理员那条,看不到用户装的
   node/git。所以 PATH 必须从 `HKLM\SYSTEM\...\Environment` 和 `HKCU\Environment` 分别读再拼。
   见 `EnvironmentProbe.BuildPath()`。
6. **`Environment.OSVersion` 不可信**(受应用清单兼容性影响),系统版本走 `RtlGetVersion`。
7. **`SetStartupState` 那种"换整个 Content"的写法会吃掉图标** —— 启动器那边的教训,
   安装器 UI 里改文字一律只改 `TextBlock.Text`。
8. **PATH 别乱写**。当前机器 PATH 已 1287 字符(接近 2047 上限),
   安装器加路径前必须查重 + 长度检查,超了就只提示不写。
9. **部署目录里的 exe 会被运行中的进程锁住**。换文件靠"改名绕过"(Rename 旧文件再复制新的),
   `.old` 残留重启后自己消失。启动器 1.3.9 就是这么换进去的。
10. **PowerShell 脚本编码**:给 Windows PowerShell 5.1 跑的 `.ps1` 必须存成 **UTF-8 with BOM**,
    否则中文全乱码、直接语法报错。
11. **`Window` 根元素的 XAML 不要用**。这个 SDK 版本下,XAML 编译器会把 `MainWindow.xaml`
    同时算进 `XamlApplications` 与 `XamlPages`,结果**不生成任何代码**(`g.cs` 是 0 字节),
    于是 `XamlRoot` 字段全找不到、`SystemBackdrop` 这种继承成员也报"类型不存在"。
    主窗口一律纯代码搭(WinUI 官方模板那样写会踩)。
12. **自定义 `UserControl` 不写 XAML 就别赋 `Content`**。没有 `InitializeComponent()` 就直接
    `Content = ...` 会在原生层崩,退出码 `0xC0000409`(STATUS_STACK_BUFFER_OVERRUN)。
    纯代码造的视觉元素用工厂方法返回 `Path` / `Grid` 这类现成控件即可。
13. **路径字符串造的 `Geometry` 不能跨 XAML namescope 用**。用 `XamlReader.Load` 造一个 `Path`
    再取它的 `Data` 赋给别的 `Path`,会抛 `ArgumentException: Value does not fall within the
    expected range`(WinRT 层,字符串里什么都没提示,极难查)。
    `XamlBindingHelper.ConvertValue` 能造出来,但丢了 viewBox 信息,`Stretch` 会按错误的包围盒
    缩放、图案偏到一边。**正解是自己解析路径迷你语法**(见 `Controls/SvgPath.cs`)。
14. **`UserControl` 当内容容器必须标 `[ContentProperty("Body")]`**。不标的话,页面 XAML 里写的
    子元素会变成 UserControl 自己的 `Content`,把整个外框(步骤条 + 标题)顶掉 ——
    表现是"每页都没有标题",而且特别难联想到原因。
15. **`RadioButton` 不适合做卡片式单选**。它的模板自带单选圆圈,即使
    `HorizontalContentAlignment=Stretch` + `Padding=0`,圆圈仍会画在左边压住卡片。
    要么自绘(`Controls/SelectableCard.cs`),要么别用。
16. **WinUI3 的 `Border` 是密封类**,不能继承。要自定义外观的容器请继承 `ContentControl`,
    里面套一个 `Border`。
17. **改留白要改对层**。`UserControl` 自己的 `Padding` 和内层 `Grid` 的 `Padding` 是两个东西,
    改错了"紧凑模式"反而更松(这个坑真踩过)。
18. **按钮可用性要在内容变化后重算**。路径框是 `Loaded` 里才填值的,而向导主窗口的
    `ApplyChrome()` 在那之前就跑过了,结果"下一步"一直是灰的。
    页面改动内容后要调 `MainWindow.RefreshChrome()`。
19. **截图验证要 DPI 感知**。PowerShell 默认是 DPI 不感知的,`GetWindowRect` 返回的是虚拟化
    尺寸(150% 缩放下 1260 会报成 840),截出来只有窗口左上角一块,会误判成"排版崩了"。
    截图脚本开头要调 `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`。
20. **`schtasks /tr` 不能塞长命令行**。带引号、带空格的完整命令行交给 `/tr` 会被拆坏,
    结果是"任务建了但没跑" —— 而且把输出吞掉就完全看不出来(实测白等 20 分钟)。
    正确做法:命令写进一个 `.cmd`,让任务指向那个 `.cmd`,并且**别吞 schtasks 的输出**。

21. **`ProcessStartInfo.EnvironmentVariables` 是区分大小写的**。Windows 环境变量名不区分大小写,
    但 .NET 这个字典区分。当前进程里那个键通常叫 `Path`,直接写 `EnvironmentVariables["PATH"]`
    会**多出一个变量**,子进程取到哪个不一定 —— 表现是"明明传了 extraPath,子进程里还是找不到 node"。
    要先不分大小写地删掉旧的,再统一用 `Path` 写回去。

22. **npm 的生命周期脚本靠 PATH 找 node**。DSH 依赖里的 `koffi` 会在 postinstall 执行
    `cmd /d /s /c node ./cnoke.cjs ...`,便携版 Node 不在系统 PATH 里就会报
    `'node' is not recognized as an internal or external command`。
    不要只靠 `extraPath`,要在一个临时 `.cmd` 里显式 `set "PATH=<node目录>;%PATH%"` 再调 npm。

23. **npm 包名里的 `^` 会被 cmd 吃掉**。版本范围 `^0.1.5-rc.1` 里的 `^` 在 `cmd.exe` 是转义字符,
    整条包名必须加引号:`"@deepseek-ai/dsh@^0.1.5-rc.1"`。

24. **进度页排版按微软《Setup》体验指南来**(原文:win32 uxguide exper-setup):
    - 当前步骤说明放进度条**上方**,写成"动词开头的短句 + 省略号"(`Copying files...`)
    - **只用一条**确定进度条,别让进度重启;单步进度折算进总进度
    - 安装期间禁用上一步/下一步,但**取消键必须可用且响应**
    - GUID、具体文件名这类"只有技术支持才关心"的细节别放界面,写进日志文件
    - 提权尽量**晚**:等用户点下"安装"这个提交动作之后再弹 UAC
    - 安装超过一分钟就别指望用户盯着看 —— 问题要攒到结束再报
25. **`IsSatisfied` 不能把 `DetectState.Optional` 算成满足**。这个枚举的语义是"**没装**但不影响使用",
    早先写成 `State == Ready || State == Optional`,结果可选组件(Git / pnpm / Python)明明没装,
    界面上却标成"已安装"、复选框还被禁用了,用户根本没法选着装。
    正解:`IsSatisfied => State == Ready`。凡是"可选"这种**描述缺失程度**的状态,一律不算满足。

26. **清单渠道别只留 raw + jsDelivr**。国内网络下 `raw.githubusercontent.com` 经常直接超时,
    jsDelivr 偶发 SSL 失败 —— 两条都挂了就完全装不上启动器(实测虚拟机连不上,报"清单拉取失败")。
    要按可达性排序并加加速前缀:`ghproxy.net` > `gh-proxy.com` > jsDelivr > raw,
    再退到 `api.github.com/repos/<owner>/<repo>/releases/latest`(响应结构不同,要单独解析)。

27. **测试脚本"文件存在就复用"会测到旧构建**。e2e 脚本本来写成"C:\DSH-Installer 在就跳过下载",
    而重置脚本**故意保留**安装器目录 —— 于是虚拟机连跑两轮都是修好之前的旧 exe,
    排查半天才发现"修的 bug 根本没进被测程序"。**每次测试都必须重新投放被测程序**,
    并且用"生成物是否存在"当指纹(例如新代码会生成 `npm-install.cmd`)。

28. **PowerShell 里 `BinaryWriter.Write($byteArray)` 会挑错重载**。它解析成 `Write(byte)`,
    只写进去**一个字节** —— 生成出来的 ico 只有 0.1 KB(实测踩过,排查了半小时)。
    必须显式给偏移和长度:`$writer.Write($payload, 0, $payload.Length)`。

29. **`System.Drawing.Icon` 读不了 PNG 帧的 ico**。`new Icon(path, 256, 256).ToBitmap()`
    会把图截成左上角一块,看着像"图标画歪了",其实是读法的问题 —— 用 DIB 帧的那些尺寸就正常。
    要肉眼验 ico,直接从字节里按目录项抠出 PNG 帧存成 .png 再看。

30. **框架依赖发布也躲不掉 `Microsoft.Windows.SDK.NET.dll`(23.7 MB)**。它是 C#/WinRT 的投影程序集,
    运行必需,压完之后大概 10 MB —— 别为了"再小一点"把它删掉,删了直接起不来。

31. **执行工作区之外的程序要放开文件策略**。本项目刻意把 .NET SDK 放在
    `G:\DeepSeek DSH\.tools\dotnet`,跟工作区是两个目录;
    在受限策略下 `dotnet` / `csc` 会直接报"拒绝访问"(不是命令写错了)。

32. **`Encoding.Default` 在 .NET Core/8 里是 UTF-8,不是 ANSI**。这是从 .NET Framework
    迁移过来最容易踩的一条:用它写 `.cmd` 时,cmd.exe 会按 GBK 读,于是**中文路径全变乱码**,
    npm 拿着一个不存在的目录去装,退出码 1 —— 而报错本身也是乱码,极难查。
    正解:`.cmd` 第一行写 `chcp 65001 >nul`,文件用 UTF-8 写,后面 cmd 就按 UTF-8 读了。

33. **Windows App Runtime 的安装包"装完不退出"**。实测它跑了 111 秒、只用 0.48 秒 CPU,
    而包早就注册好了,就是挂在那儿。**别死等退出码** —— 轮询"运行库真的好用了吗",
    好了就走;另外必须有硬超时兜底。死等的结果是进度条永远停在某个百分比。

34. **aka.ms 的 `latest` 可能比自己的 SDK 包还旧**。它给装上了 `8000.921.1539.0`,
    而 WindowsAppSDK 1.8.260804001 要 `8000.946.1701.0`,安装器照旧起不来。
    所以探测**不能只看"有没有装",必须比最低版本**,下载也要**精确版本优先**。

35. **中断安装不会收掉子进程**(Windows 不会)。安装器被关掉之后,它拉起来的
    `npm install` 会在后台继续跑 —— 实测留过 5 个 node、1.4 GB 内存、还都往同一个目录里写。
    正解:`ProcessChildGuard`(Job Object + `KILL_ON_JOB_CLOSE`),句柄一关整棵树一起走。

36. **`AppWindow.Closing += ...` 别写在拖拽回调里**。之前它被误放进标题栏的拖拽处理函数,
    于是**只有拖动过窗口之后**才装得上关闭拦截 —— 没拖过就直接关掉,回滚根本没机会跑。

37. **多个安装器实例会互相踩**。它们共用 `%TEMP%\DSH-Installer`,并发解压同一个目录会
    `Access to the path ... is denied`。用户连点/反复重试很容易造出这种局面 ——
    现在有了子进程陪葬,残留不会累积;但**连点仍然要避免**。

38. **回滚只撤"本次安装自己创建的"目录和 PATH 条目**(`NoteCreatedDirectory` / `NotePathEntry`)。
    重复装到同一个目录时,从第二次起那些目录**本来就存在**,按设计不会动它们 ——
    用户看到的"回滚了但文件夹还在"多半是这一条,不是回滚坏了。
    所以回滚现在**无条件写一行检查日志**(创建了什么/PATH 改了什么),免得再猜。

39. **页面顺序和默认值之间有依赖,要显式写清**。DSH 位置页现在排在组件页**前面**,
    因为组件目录的默认值就是 `<DSH 目录>\components`;顺序一换,这个默认值才拿得到。
    同理"可选组件排在必选之后"和"PATH 要包含可选组件目录"是冲突的 ——
    解法是让 PATH 那一步**不要求目录当下存在**,而不是把可选组件提到 PATH 前面。

40. **`PathAccess` 判"这个目录要不要管理员"用真写文件、不用路径白名单**。
    白名单永远列不全(ACL、组策略、只读盘、网络盘);试写一个临时文件再删,才是准的。
    它同时喂给两处:向导里的提示,以及 `NeedsElevation`(选了 Program Files 就照样提权,
    否则会一路写失败 —— 实测 Node 和 DSH 本体双双"失败")。

---

## 无人值守与虚拟机测试

安装器支持无人值守,虚拟机上的端到端测试就是靠它做的。

### 命令行开关

```
--silent                     跳过向导直接装
--uninstall                  以卸载模式启动(配合 --silent 可无人值守卸载)
--scope=machine|user         安装范围(默认 user)
--source=china|official      下载源偏好(默认 china)
--dsh-root= / --launcher-root= / --components-root=
--git --pnpm --python        装对应可选组件
--no-shortcut --no-autostart --no-launch
--no-runtime                 不装运行库(默认会装 .NET 8 桌面运行时与 Windows App Runtime)
--report=<文件>              把结果写成 JSON(给自动化读)
--dry-run                    演练模式,不落盘
--page=N                     跳到第 N 页预览界面
--plan=<文件>                提权实例回读向导里填好的选项
--install                    显式允许"带 --page 跳页时也真安装"
```

**安全约定**:带 `--page` 一律强制演练模式。开发时在本机翻页看样式绝不会误装东西。

### 提权

选"为所有用户安装"或勾了"开机自启"时,点下"安装"那一刻**一次性提权**:
把选项落盘 → `runas` 起一个提权实例 → 当前实例退出。
提权实例读回选项后直接进进度页,所以全程只有一次 UAC,向导也不用重填。
仅为本用户且不要自启时,全程不需要管理员。

### 测试脚本(在 `DSH Works\.fix-lasso-state\vm-share\`)

| 脚本 | 用途 |
|---|---|
| `reset-vm.ps1` | **反复测试用**:清掉上一次的全部痕迹(目录 / 计划任务 / 快捷方式 / Run 键 / PATH 条目 / 日志)。加 `-RemoveInstaller` 连安装器一起删 |
| `e2e-install.ps1` | 用户范围核心路径:Node → DSH 本体 → 启动器 → 快捷方式 → PATH |
| `e2e-machine.ps1` | 全局范围:装到 `Program Files`,含快捷方式与开机自启,验证机器级 PATH |
| `e2e-uninstall.ps1` | 卸载验证:确认目录 / 任务 / 快捷方式 / PATH 条目 / 状态文件都被清掉 |
| `diag-npm.ps1` / `net-test.ps1` | 出问题时的诊断:安装器日志、npm 调试日志、各下载通道可达性 |

配套:`run-e2e-bg.ps1`(主机侧)触发并等待结果,`vm-server.ps1` 提供 HTTP 分发(端口 8899)。

### 虚拟机 SSH 直连(2026-09-19 打通)

虚拟机:`192.168.188.130`(VMnet8),用户 `KitamaruRamito`,Windows 10 1809 build 17763。
主机侧私钥 `~\.ssh\dsh_vm`,已配免密。

主机侧跑远端命令用这个包装(它把脚本编成 UTF-16LE base64 再走 `-EncodedCommand`,
避免引号和中文被"本地 shell → ssh → 远端 cmd → 远端 powershell"一层层剥烂):

```powershell
& 'G:\DeepSeek DSH\DSH Works\.fix-lasso-state\vm-ssh.ps1' -Script 'hostname; $PSVersionTable.PSVersion'
```

**装 sshd 的四层坑**(一层修完下一层才露头,`vm-share\fix-sshd.ps1` 是最终能用的版本):

| # | 现象 | 原因 | 解法 |
|---|------|------|------|
| 1 | `Add-WindowsCapability` 装不上 | 它的源是 Windows Update,而测试期间更新是关掉的 | 别走能力包,直接下 Win32-OpenSSH 官方 zip(镜像优先) |
| 2 | `install-sshd.ps1` 半路抛异常 | 那版 `OpenSSHUtils.psm1:624` 给 `FileSystemRights` 传了 `268435456`,老系统上直接抛;它一挂,后面的服务特权/权限/配置复制全不做 | 不用它的脚本,所有步骤自己来 |
| 3 | 服务 `START_PENDING → Stopped`,**日志一片空白** | `%ProgramData%\ssh` 上 `Users` 那条是**显式** ACE,`icacls /inheritance:r` 只删继承来的,删不掉;主机密钥由此被 sshd 拒收:"Bad permissions ... no hostkeys available" | 显式 `/remove:g`;而且服务方式跑时 sshd 的 stderr **全被丢掉**,必须用 SYSTEM 身份前台跑才看得见 |
| 4 | 密钥权限改干净了还是被拒 | 目录上有 `CREATOR OWNER:(OI)(CI)(IO)F`,密钥是拿登录账号生成的,于是**所有权**落在那个账号上 → `Bad permissions. Try removing permissions for user: ...\KitamaruRamito` | 改用 .NET ACL API:断继承、丢条目、只留 SYSTEM/Administrators、**`SetOwner(SYSTEM)`** |

还有:注册服务必须补上官方脚本里那行

```
sc.exe privs sshd SeAssignPrimaryTokenPrivilege/SeTcbPrivilege/SeBackupPrivilege/SeRestorePrivilege/SeImpersonatePrivilege
```

少了它 sshd 一启动就自己退出。

**排查心法**:`sshd -t` **不校验文件权限**(所以它一路"通过"很有迷惑性);
服务起不来看不到任何输出时,用 `schtasks /ru SYSTEM` 把 `sshd.exe -ddd > 文件 2>&1` 跑一遍,这是唯一能看到真话的办法。

> ⚠️ 回滚快照会把 sshd 一起滚掉,重装一遍 `vm-share\fix-sshd.ps1` 即可(幂等)。

### 三条铁律(都是踩出来的)

1. **测试前必须重新投放被测程序**。脚本曾经写成"C:\DSH-Installer 在就跳过下载",
   而重置脚本故意保留安装器目录 —— 连跑两轮都是旧 exe,排查半天空欢喜。
2. **开跑前强制删掉上次的报告文件**。否则"等报告"的循环会瞬间退出,把旧结果当成新的。
3. **`schtasks /tr` 不能塞长命令行**,而且**别吞它的输出**。命令写进 `.cmd`,任务只指文件。

## 引导程序与正式打包

| 文件 | 说明 |
|------|------|
| `boot\Boot.cs` | **真引导**(不只是自解压)。.NET Framework 编译,自己没有任何前置依赖。流程:① 探测 .NET 8 桌面运行时与 Windows App Runtime 1.8,缺了就 `runas` 起一个 `--runtimes-only` 的副本去下载 + 静默安装(带进度窗),装不上就弹提示并提供官方下载页;② 扫自身末尾的 EOCD 签名(0x06054B50)反推 zip 起点,解压到 `%LOCALAPPDATA%\DeepSeekHarness\Boot`;③ 拉起里面的 `DSH-Installer.exe`,命令行原样传下去;④ 等它退出。引导阶段窗口一闪而过,所以每一步都写 `%TEMP%\dsh-boot.log` |
| `boot\Uninstall.cs` | **独立卸载程序 `DSH-Uninstall.exe`**。找保留的那份安装器 → 把它连同自己复制到 `%TEMP%` 再运行(原地跑的话卸载流程删不掉自己所在的目录,文件被运行中的进程锁着)→ 需要时申请一次提权 → 跑完清掉临时副本。参数原样转给安装器并补上 `--uninstall`,所以 `DSH-Uninstall.exe --silent` 就是无人值守卸载 |
| `tools\make-icon.ps1` | 生成 `assets\DSHInstaller.ico`(圆角蓝底 + 白色鲸鱼,7 个尺寸:16/24/32/48/64 用 DIB 帧,128/256 用 PNG 帧) |
| `pack-release.ps1` | **正式打包**:框架依赖发布 → 补 `.xbf`/`.pri` → 校验 → 编 `Boot.exe` 与 `DSH-Uninstall.exe`(csc)→ 把卸载器塞进发布目录 → 打 `payload.zip` → 二进制拼接成**单个 `DSH-Installer-Setup.exe`** → 自检(payload 起始字节必须是 `50 4B 03 04`)+ 打印 SHA256 → 拷桌面 |

**体积**(这是这一轮最大的变化):

| | 之前 | 现在 |
|---|---|---|
| 发布方式 | 自包含(`--self-contained true` + `WindowsAppSDKSelfContained=true`) | **框架依赖** |
| 运行库 | 打进包里 | **必选组件,装的时候在线下载 + 静默安装** |
| Setup.exe | 119.4 MB | **10.5 MB** |

**引导链路的实测日志**(本机跑 `Setup.exe`,已完成):

```
12:45:06.783  === boot started ===
12:45:06.816  运行库齐全:.NET=8.0.11 / WinAppRuntime=8000.946.1701.0   ← 都在,所以一次 UAC 都没弹
12:45:06.822  extracting to C:\Users\...\AppData\Local\DeepSeekHarness\Boot
12:45:06.824  zipStart = 71168          ← 正好等于 Boot.exe 的字节数
12:45:06.844  extracting zip (10887073 bytes) ...
12:45:07.209  extract done              ← 10.9 MB 约 0.4 秒
12:45:07.214  starting: ...\Boot\DSH-Installer.exe
12:45:09.375  installer exited with 0
```

**卸载器实测**(`dist\DSH-Uninstall.exe --silent --dry-run`,已完成):

```
12:45:19.228  installer home = C:\Users\...\AppData\Local\DeepSeekHarness\Boot
12:45:19.230  copying installer to C:\Users\...\Temp\dsh-uninstall-bf25fc9a...
12:45:19.345  running: ...\DSH-Installer.exe --uninstall "--silent" "--dry-run"
12:45:21.141  installer exited with 0      ← 临时副本已自动清理,保留目录没被动
```

**安装计划顺序**(`BuiltInSteps.BuildPlan`,已实测报告):

```
runtime-dotnet → runtime-winapprt → node → dsh → launcher → uninstaller
  → shortcut → path → autostart → (可选 git / pnpm / python)→ verify
```

前九个是必选,可选的三个夹在必选全做完之后、收尾校验之前 —— 可选组件失败不该挡住"能用的 DSH"。

### 自包含模式留下的坑(留个记录,别再踩)

`WindowsAppSDKSelfContained=true` 时**必须**跳过 `Bootstrap.Initialize`,
不然一启动就 `0xC000027B`(STATUS_STOWED_EXCEPTION),而且在它自己目录里直接跑也一样崩,
很容易误判成"打包坏了"。现在走框架依赖,这条路已经不走;`Program.cs` 里那段 `#if` 常量从没定义过,
是死代码,下次清理时可以删掉。

## 收尾欠账

1. ~~图标~~ ✅ 已做(`assets\DSHInstaller.ico`,由 `tools\make-icon.ps1` 生成)
2. ~~双语 README~~ ✅ 已做(`README.md`,中英逐节对照)
3. ~~卸载器独立 exe~~ ✅ 已做(`boot\Uninstall.cs` → `DSH-Uninstall.exe`)
4. ~~运行库改成必装项~~ ✅ 已做(引导程序按需下载 + 静默安装;安装包 119.4 MB → 10.5 MB)
5. ~~必选项/可选项重排~~ ✅ 已做(组件页改成必选在上、可选在下;安装计划也是必选在前)
6. **图标观感复核** —— 现在的鲸鱼图标是本鲸娘用 GDI+ 画的,真实任务栏/开始菜单里好不好看没看过
7. **在干净虚拟机上端到端验证这一轮改造** —— 特别是"两个运行库都没装的机器"这条路径
   (本机两个都有,所以**下载安装运行库那段代码从没真跑过**,只跑过"已就绪,跳过")
8. **GitHub 上传** —— **等用户指令**,不要自己推

### 已知没验到 / 验不了的(诚实记录)

- **运行库下载安装这段没真跑过**。本机 .NET 8.0.11 与 WinAppRuntime 8000.946.1701.0 都在,
  引导程序走的是"已就绪"分支。要验它得找一台两样都没有的干净虚拟机。
- **`ResolveDotNetDesktopUrl` 的直链也没验过**。开发机上外网被沙箱挡着,只验证过代码路径不崩。
  候选地址有三条( release-metadata 解析出的具体版本 → aka.ms 稳定通道 → 写死的 8.0.11),
  下载引擎会自己轮换,但"至少有一条能通"这件事必须在能联网的机器上确认。
- **"无运行时"场景没验成**。虚拟机上 `QuietUninstallString` 卸载 .NET 退出码是 0 但运行时一个都没少;
  `Remove-AppxPackage` 也删不掉 `WindowsAppRuntime`(系统框架包得走 DISM)。所以那次测试**没测到真东西**,别当它通过。
- **回滚没手动验过**。用户说自己点取消测,本鲸娘没代劳。回滚走的是 `UninstallSteps`(与卸载器同一套代码,那套已 8/8 通过)。
- **提权的 UAC 弹窗没实测过**。无人值守路径全程在管理员计划任务里跑,`IsElevated()` 恒为真,走不到弹窗那一步。
- **状态文件的两个新字段只有"真装"才会写入**。`InstallerHome`(安装器保留目录)和
  `UninstallerPath`(卸载程序完整路径)是 `uninstaller` 步骤写的,本机只跑过 `--dry-run`,
  所以实际落盘没看过。卸载器那边有"固定落脚点 `%LOCALAPPDATA%\DeepSeekHarness\Boot`"兜底,理论上不缺。

## 目录结构

```
DSH Installer\
├─ STATUS.md                  ← 你正在看的这份
├─ README.md                  用户向双语说明(已完成)
├─ Directory.Build.props      版本号 / 包名 / 门槛常量,全项目共用
├─ build.ps1                  开发期构建:UI + 引导 + 卸载器 + 打包启动器 payload(不合成 Setup)
├─ pack-preview.ps1           开发用:发布到 dist\preview 并打成 zip(补 .xbf / 校验资源数)
├─ pack-release.ps1           正式用:框架依赖发布 + 编引导/卸载器 + 拼成单个 Setup.exe,可 -NoDesktop
├─ assets\
│   └─ DSHInstaller.ico       图标(用 tools\make-icon.ps1 生成,别手改二进制)
├─ docs\
│   ├─ SETUP.md               开发环境与构建说明
│   ├─ ARCHITECTURE.md        页面流程 / 数据流 / 关键类 / 为什么需要 Boot
│   ├─ CHANGELOG.md           版本记录
│   └─ 文案清单.md             界面全部文案的中英对照(用户改中文、本鲸娘补英文)
├─ payload\
│   └─ launcher.zip           启动器兜底包(build.ps1 生成,不进 git)
├─ boot\
│   ├─ Boot.cs                真引导:装运行库 + 自解压 + 拉起安装器(csc 直编,不是 .NET 工程)
│   └─ Uninstall.cs           独立卸载程序(csc 直编)
├─ tools\
│   ├─ Probe\                 命令行体检工具(验证检测逻辑用)
│   └─ make-icon.ps1          生成 assets\DSHInstaller.ico
├─ dist\                      构建输出(不进 git)
└─ src\
    ├─ DshInstaller.Shared\   共享库(检测/下载/安装/提权/状态)
    │   ├─ WellKnown.cs
    │   ├─ ConfigStore.cs         状态文件 + 日志
    │   ├─ DshLocator.cs          找 DSH 装在哪
    │   ├─ ElevationHelper.cs     是否管理员 / 自我提权
    │   ├─ PowerShellScript.cs    跑 PS 拿结构化结果
    │   ├─ ShellLink.cs           读写 .lnk
    │   ├─ Detection\
    │   │   ├─ ComponentStatus.cs     检测结果模型
    │   │   ├─ EnvironmentProbe.cs    整机体检
    │   │   └─ RuntimeProbe.cs        两个必装运行库(启动早期就要用)
    │   └─ Install\
    │       ├─ InstallOptions.cs      安装选项(目录/勾选/演练)
    │       ├─ InstallStep.cs         步骤 + 上下文 + 进度上报
    │       ├─ InstallRunner.cs       顺序执行 + 补救重试轮
    │       ├─ BuiltInSteps.cs        全部安装步骤 + BuildPlan(必选在前)
    │       ├─ UninstallOptions.cs    卸载的逐项开关
    │       ├─ UninstallSteps.cs      卸载/回滚共用的撤销步骤
    │       ├─ PathEditor.cs          PATH 增删查(带长度预检)
    │       ├─ LauncherFeed.cs        启动器清单 / 镜像前缀 / API 兜底
    │       ├─ ProcessRunner.cs       跑外部命令
    │       ├─ DownloadEngine.cs      多源轮换 / 续传 / 停滞检测
    │       ├─ MirrorSource.cs        官方与镜像地址(含运行库直链与版本解析)
    │       └─ ArchiveExtractor.cs    zip / 7z / tar.gz
    ├─ DshInstaller\          WinUI3 主程序(10 个页面 + Controls / Styles / i18n)
    └─ DshInstaller.Boot\     ⚠️ 空目录,历史遗留。引导早就挪到顶层 boot\ 了,可以删
```

---

## 进度

| 模块 | 状态 | 备注 |
|------|------|------|
| 决策与大纲 | ✅ 完成 | 见上面决策表 |
| 项目骨架 | ✅ 完成 | 目录 + props + build.ps1 |
| Shared:检测(静态) | ✅ 已编译 + 实机验证 | `EnvironmentProbe` / `DshLocator` / `ComponentStatus` |
| Shared:运行库探测 | ✅ 完成 | `RuntimeProbe`,启动早期判"要不要提权"也用它 |
| Shared:下载引擎 | ✅ 代码写完 | 多源回退 + 断点续传 + 速度 |
| Shared:解包 | ✅ 代码写完 | zip 优先 .NET,7z 走 7z.exe 或 tar.exe |
| Shared:启动器发布源 | ✅ 代码写完 | `LauncherFeed`:清单解析 + 镜像前缀 + 回退 |
| Shared:提权/状态/日志/快捷方式 | ✅ 代码写完 | |
| 开发工具 `tools\Probe` | ✅ 可用 | 命令行体检,验证检测逻辑 |
| Shared:组件安装编排 | ✅ 完成并在 VM 上验证 | `BuiltInSteps` 全部真实步骤 + `InstallRunner` 顺序执行 |
| Shared:运行库必装步骤 | ⚠️ 代码写完,**只跑过"已就绪,跳过"** | `runtime-dotnet` / `runtime-winapprt`;下载安装那段本机没条件验 |
| Shared:卸载逻辑 | ✅ 完成并 VM 验证 | `UninstallSteps`,卸载器那套 8/8 通过;新增 `registry` 步骤 |
| Shared:卸载入口部署 | ⚠️ 代码写完,只跑过 dry-run | `uninstaller` 步骤:保留安装器 + 铺卸载程序 + 注册"应用和功能" |
| UI:框架与导航 | ✅ 完成 | 自绘标题栏 + Frame + 步骤条 + 页脚动作按钮 |
| UI:10 个页面 | ✅ 完成 | 组件页已改成"必选在上、可选在下" |
| 引导 Boot.exe | ✅ 完成并实测(本机) | 真引导:运行库探测 + 自解压 + 拉起安装器 |
| 独立卸载器 | ✅ 完成并实测(本机) | `DSH-Uninstall.exe`,复制到 %TEMP% 再跑,支持 --silent |
| i18n 中英 | ✅ 完成 | 46+ 条 key,中文书面语 / 正式英文 |
| 图标 assets | ✅ 完成 | `tools\make-icon.ps1` 生成,7 个尺寸;观感待复核 |
| README(双语) | ✅ 完成 | 341 行,中英逐节对照 |
| 正式打包 pack-release.ps1 | ✅ 完成并实测 | 产出 10.5 MB 单文件 Setup.exe,含自检 |
| GitHub 上传 | ⏳ 未开始 | **等用户指令** |

### 已经验证过的事

- **共享库编译通过**:`dotnet build src\DshInstaller.Shared -c Release` → 0 警告 0 错误
- **检测引擎实机跑通**(`tools\Probe`):
  本机 Windows build 22635,需要的东西全绿,DshLocator 一次命中 `G:\DeepSeek DSH`
- npm 双源可达、Node 22 最新 LTS = `v22.23.2`(镜像源也能查到)
- 本机已有:Node v26.7.0 / npm 11.19.0 / pnpm 12.4.1 / Git 2.50.1 / Python 无(只有商店占位)
- 本机已有 .NET 8 (8.0.11) 和 Windows App Runtime 1.8 (8000.946.1701.0)
- 踩到并修掉:WindowsApps 里那个 `python.exe` 是应用商店占位程序,调它会弹商店,
  已加过滤(`EnvironmentProbe.ProbePython`)。

### 启动器可移植性(2026-09-18 补)

安装器要装在**别人**机器上,所以启动器里写死开发机路径是致命的。已修:

| 问题 | 原来 | 现在 |
|------|------|------|
| DSH 根目录 | 写死 `G:\DeepSeek DSH` | `LauncherLocator`:环境变量 `DSH_ROOT` → 同目录 `launcher.json` → 向上找 `bin.js` → 常见位置 → 记录文件 |
| node.exe | 写死 `E:\Nodejs\node.exe` | `LauncherLocator.FindNode()`:环境变量 `DSH_NODE` → 配置 → DSH 自带的 `node_modules\node\bin\node.exe` → PATH → 常见位置 → 注册表 |
| launcher.log | 写死 `G:\DeepSeek DSH\logs` | `%LOCALAPPDATA%\DeepSeekHarness\launcher.log` |
| last-url.txt | 写死根目录 | 走动态找到的根目录 |
| `Constants.DefaultRoot` / `DefaultNode` | 兜底写死 | **已删除**,找不到就明确报错写启动日志 |

配套新增:**启动器仓库的 `verify.ps1`**,静态自检(关键文件/运行库/写死路径/`.old` 残留)。
启动器版本因此升到 **1.3.10**。

### 虚拟机端到端验证结果(2026-09-19)

干净虚拟机上实跑,报告文件 `succeeded: true`。

| 范围 | 结果 | 关键证据 |
|------|------|----------|
| 用户范围 | ✅ 七步全过 | `C:\InstallTest\components\node` 94.9 MB;`DSH` 260.5 MB;启动器 37.2 MB;用户 PATH 写入生效 |
| 全局范围 | ✅ 七步全过 | 装到 `Program Files`;桌面快捷方式;开机自启计划任务 `RunLevel=Highest / Ready`;机器级 PATH 写入 |

每个步骤都落盘可查(`%LOCALAPPDATA%\DeepSeekHarness\installer.log`),
安装状态写入 `installer-state.json`,用户 PATH 与计划任务都用独立探针复核过。

**下载链路实测**:Node 走 `npmmirror` 20 秒;npm 装本体 520 个包约 2 分钟;
启动器清单在虚拟机上直连失败 → 自动退到 GitHub API → 走 `ghproxy.net` 下包 → SHA256 校验通过。

### 还没验证的事(诚实记录)

- ~~启动器没在干净机器上跑过~~ **已作废(2026-09-19)**:干净虚拟机(Win10 17763)上从零装完整套,
  `LauncherFeed` 清单直连失败后自动退到 GitHub API、走 `ghproxy.net` 下到启动器 1.3.21、
  SHA256 校验通过、解压落地 —— 见上面"虚拟机端到端验证结果"。这条当时写下的顾虑已经不成立了。
- 仍然成立的是:**启动器跑起来之后**能不能真正拉起 DSH、托盘与自更新是否正常,
  安装器不管这一段(那是启动器自己仓库的测试范围,本项目只负责把它正确装到位)。

### 开发小工具

```powershell
cd 'G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup'
$env:NUGET_PACKAGES = 'G:\DeepSeek DSH\.nuget-packages'
& 'G:\DeepSeek DSH\.tools\dotnet\dotnet.exe' run --project .\tools\Probe -c Release
```

会把系统版本、每个组件的检测结果、DSH 候选路径、Node LTS 版本全打出来。
界面还没做的时候,改检测逻辑就靠它验证。

---

## 下次开工第一件事

**当前这一轮的代码全部编译通过、打包产出了**(`dist\DSH-Installer-Setup.exe`,10.5 MB,桌面也有一份),
所以下一步不是写代码,而是**在干净虚拟机上验证**:

1. 先把虚拟机重置干净(`DSH Works\.fix-lasso-state\vm-share\reset-vm.ps1`,加 `-RemoveInstaller`),
   **确认两个运行库都不在**(那是本轮唯一没真跑过的路径);
2. 拖桌面上的 `DSH-Installer-Setup.exe` 进去双击;
3. 看 `%TEMP%\dsh-boot.log` —— 应该能看到"缺少运行库" → 提权 → 下载 → 安装 → 解压 → 拉起安装器;
4. 装完之后检查:启动器目录里的 `DSH-Uninstall.exe`、"应用和功能"里的卸载项、以及点它能不能卸干净。

改代码之前先跑一次全量编译确认没烂:

```powershell
cd 'G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup'
$env:NUGET_PACKAGES = 'G:\DeepSeek DSH\.nuget-packages'
& 'G:\DeepSeek DSH\.tools\dotnet\dotnet.exe' build .\src\DshInstaller\DshInstaller.csproj -c Release
```

要出包就 `.\pack-release.ps1`(它会自己编共享库、发布、编引导和卸载器、合成 Setup.exe、拷桌面)。
**别在没编译过的代码上继续堆。**

---

## 数据流(简版)

```
欢迎页
  └─ 系统门槛检查(build < 17763 直接劝退)
安装范围页(user / machine)
  └─ machine 才需要提权
环境检测页  EnvironmentProbe.Run()
  ├─ 全绿 → 跳到"启动器位置页"
  └─ 有缺 → "组件安装页"
组件安装页
  ├─ 选组件根目录 + 下载源
  ├─ 先补必装运行库:.NET 8 桌面运行时 / Windows App Runtime(缺才下,静默装)
  └─ 逐个:DownloadEngine → ArchiveExtractor → 配置(PATH/npmrc)
DSH 位置页
  ├─ 已检测到 → 直接用
  └─ 没有 → 选目录 → npm install @deepseek-ai/dsh node@^24
启动器位置页(默认 <DSH 根>\DeepSeek Harness)
确认页 → 进度页 → 完成页(立即启动 / 开机自启 / 桌面快捷方式)
```

进向导之前还有一层:用户双击的其实是 `DSH-Installer-Setup.exe`(引导 + 附加 payload),
它先保证运行库到位,再把安装器解压到 `%LOCALAPPDATA%\DeepSeekHarness\Boot` 拉起来。
装完之后那份副本**故意保留**,加上铺到启动器目录的 `DSH-Uninstall.exe`,卸载才有入口。

细节见 `docs/ARCHITECTURE.md`。

---

## 2026-09-19 夜:本轮改了什么(VM 实测驱动)

这一轮全部来自虚拟机实测反馈,每条都对应一个**用户看得见的现象**。

### 引导程序(Boot.exe)

| 现象 | 原因 | 处置 |
|------|------|------|
| 缺运行库直接开下,没打招呼 | 设计上没问 | 加确认弹窗(缺哪几个、是否现在下载);`--silent` 不弹,免得卡在没人点的框上 |
| 进度条反复重启、没有总进度 | 每件事各走一根条 | 改成**一根总条**按权重折算;阶段标题带 `(2/4)` 序号 + 速度/字节/剩余时间 |
| 卡在 0%「正在连接下载服务器…」 | **WebClient 没有超时** | 加 40 秒无数据看门狗,掐掉换源 |
| `windowsappruntimeinstall.exe` 装完不退出,进度条永远停在 61% | 死等退出码 | 改成**轮询"运行库真的好用了吗"**,好了就走;外加 15 分钟硬超时 |
| 装完仍报 "Required components ... missing" | aka.ms 的 `latest` 给的是 8000.921,而 SDK 要 8000.946 | 探测**必须比最低版本**;下载**精确版本优先** |
| 装完立刻重开安装器报"解压后没有找到安装程序" | 旧目录删不掉时会换带时间戳的目录,主流程却还去固定路径找 | 记下**实际用的目录** |
| 探测分段总是失败、.NET 永远单连接 | **TLS 1.2 设得太晚**(`ServicePointManager` 是进程级的) | 挪到进程启动第一件事;探测超时 15→25 秒,失败**落盘** |
| 装完不清理临时目录 | 以前刻意留着给卸载器用 | 退出时清理,但**有别的实例在跑就不删**;长期副本改放 `<DSH 根>\.installer` |

另外:Boot 现在也走 **8 连接分段下载**(运行库那两个包 58MB / 102MB 受益最大),
拿不到 Range 或失败就原样回落单连接。

### 安装向导

- **文案全书面语化**(60 处),去掉"顺手""往下走"这类口语
- 组件页改**左右两列**(必选在左),不再因为内层滚动条把"可选"顶出屏幕
- 页面顺序:**DSH 位置 → 组件**(组件目录默认值就是 `<DSH 根>\components`)
- 目录需要管理员权限时**当场提示**,并且 `NeedsElevation` 也认这个 → 不会再"Node 和 DSH 本体双双失败"
- 右键/Alt+F4 关闭会**先回滚**:`AppWindow.Closing` 订阅以前被误写在**拖拽回调**里,只有拖过窗口才生效
- 计划列表只列**这次真要做的事**:快捷方式/PATH/开机自启没勾就**不排**,不再显示"未勾选,跳过"
- 「安装完成后立即启动」以前**根本没实现**(只记了个布尔值);现在点"完成"时按复选框启动

### 下载

- 开下前**实测各源速度**并排序;测到 ≥1 MB/s 直接开下,不再白等其余源
- 测速结果**按域名缓存**,一次运行只测一遍
- **8 连接分段下载**:每段独立重试并换源,合并后校验大小;任何一步不顺就回落单连接
- 文案/单位统一:`已下载 X / Y · 速度 · 预计还需…`,**过 1024 就进位**(不再出现 `18667 KB/s`)
- 镜像顺序按实测重排(`gh-proxy.com` 提到第一),剔掉实测超时的源

### 卸载

- **卸载向导从来没把路径传给步骤** —— 界面显示着路径,`options` 里三个全是 null,
  于是逐个"目录未记录,跳过",**一个字节没删**,而跳过只写界面不落盘。现已修,并把跳过**落盘**
- **卸载器删不掉自己**:`DSH-Uninstall.exe` 躺在 DSH 根目录里、自己又在运行 → 现在**拉起安装器就退出**
- 整棵树一次性删 → 改**逐项删**,一项失败不再拖累整棵;失败先重试一次,仍失败**降级成警告**(卸载语义是"能清多少清多少")
- 卸载完成页以前会显示**安装**那套文案("安装未完全完成 / 未安装（可选）")—— `OnLoaded` 覆盖了 `ApplyText` 的结果
- 开始菜单快捷方式**一并删除**(连空目录)

### 其它

- 图标换成**官方鲸鱼**(从 `DshBrandData.MarkPath` 解析渲染,和界面里用的是同一份路径)
- 文件夹选择器**真做了**(`IFileOpenDialog` + `FOS_PICKFOLDERS`);以前三个页面都只弹"选择器出现问题"
- npm 中文路径修复:`.cmd` 改用 `chcp 65001` + UTF-8 写
  (`.NET Core 的 Encoding.Default 是 UTF-8`,cmd 按 GBK 读 → 路径乱码 → npm 退出码 1)
- 子进程挂 **Job Object**(`KILL_ON_JOB_CLOSE`):安装器被中断不会再留下野 node
- 注册表里记 `DshRoot`/`LauncherRoot`/`ComponentsRoot`/`PathEntries`;
  `ConfigStore.Load()` 读不到状态文件就**退到注册表**
- 启动器那步写 `launcher.json`(`dshRoot` + `nodePath`)—— PATH 改了但**已在跑的进程不会重读**

## 约定(写代码时遵守)

- C# 用 `net8.0-windows`,不用 `ImplicitUsings`,不用 nullable,风格跟启动器保持一致(显式 `delegate {}`、不用 LINQ 链式堆叠)。
- 注释写**为什么**,不写"这里赋值给 x"。
- 所有会失败的操作都要有兜底和日志(`InstallLogger.Write`)。
- 任何"改注册表/写 PATH/建快捷方式"的动作,都要在演练模式(`--dry-run`)下可跳过。
- 界面文案一律走 i18n key,不许硬编码中文。
