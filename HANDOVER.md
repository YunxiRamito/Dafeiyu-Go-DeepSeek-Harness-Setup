# 交接:大肥鱼Go / Dafeiyu-Go

> 给下一个接手的人(或下一个 AI)。当前发布版本为 `1.4.9`，工作版本为 `1.4.9.1`，启动器与安装器
> 已改为同版本同步发布。第一入口请先读父级
> `G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\HANDOVER.md`，再读本文件、
> `STATUS.md`、`RELEASE.md` 和 `TRANSITION.md`。

最后更新:2026-09-23

---

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

当前阻塞：

- SignPath 尚未配置。安装器仓库没有 `SIGNPATH_API_TOKEN` secret，
  也没有五个 SignPath repository variables，tag 发布会直接失败。
- 不应绕过签名发布正式 `v1.4.9.1`。

下一步：

1. 配置 SignPath 仓库变量和 secret。
2. 在虚拟机完整验证安装、取消回滚、推荐插件 4 线程下载和卸载。
3. 推送 `v1.4.9.1` tag，等待签名的 Setup 和启动器 ZIP 发布完成。

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
