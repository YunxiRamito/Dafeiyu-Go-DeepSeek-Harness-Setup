# 大肥鱼Go安装器发布指南

## 当前发布规则与必做清单（2026-10-06）

当前源码 1.5.4 正在验收准备，尚未发布；以下流程是发布要求，不是已完成记录。

本节优先于下方历史命令示例。安装器与启动器同版本同步，正式发布走 tag CI；本地 `pack-release.ps1 -NoDesktop` 只构建，不表示已发布。先确认安装器 CI 成功，再发布启动器同版本 tag，随后验证 npm / npmmirror 和公开清单。签名以当前 `SIGNING.md` 和工作流配置为准，不绕过已配置策略。

**每次发布必须同步修改两个 GitHub Release 的更新日志。只上传安装包、只提交 CHANGELOG 或使用 CI 自动提交列表都不算完成。**

- 推送 tag 前：更新本仓库 `docs/CHANGELOG.md` 和启动器 `CHANGELOG.md`，说明两边用户可见改动、验证与已知限制；版本、README 一并提交。
- CI 完成后：分别修改安装器与启动器 GitHub Release 标题及正文，使用同一批产品更新说明，保留各自实际文件名、SHA-256 和签名状态。
- npm / 镜像：只有实际下载并核对包内 ZIP 与 Release 哈希一致，才能写“镜像已同步”；再更新启动器清单，不能拿本地验证包哈希代替 CI 包哈希。
- 发完读回：通过 GitHub 页面或 API 检查两个 Release 的版本、标题、正文、资产、哈希和限制说明。发现仍是默认自动正文就立即补写，不留到下次。
- 最后回填：更新两仓库 HANDOVER / STATUS，提交推送文档并核验公开下载入口。**两个 GitHub 更新日志未验证之前，不得宣布同步发布完成。**


> 配套读:启动器仓库的 `RELEASE.md`(那边讲怎么发启动器,这边讲怎么发安装器)。
> 两份是对称的 —— 装的人拿到的是安装器,安装器再去启动器仓库拿启动器。

---

## 零、这个仓库发什么

一个文件:

```
DSH-Installer-Setup.exe     单个 exe,双击即用(约 11 MB)
```

它是**引导程序 + 附加的 payload** 拼出来的:引导程序用 .NET Framework 写
(Windows 自带,没有任何前置依赖),payload 是框架依赖发布的 WinUI3 安装器。
缺的 .NET 8 桌面运行时与 Windows App Runtime 由引导程序**按需下载安装**,
所以包才这么小 —— 别把运行库打进去,那会变成 119 MB。

**版本号只有一个地方写**:`Directory.Build.props` 里的 `<InstallerVersion>`,
别处(程序集版本、界面显示、Release 标题)都引用它。

---

## 一、省事版:一条命令

```powershell
cd 'G:\DeepSeek DSH\DSH Works\Project\Dafeiyu-Go\Dafeiyu-Go-DeepSeek-Harness-Setup'
.\pack-release.ps1
```

它会:框架依赖发布 → 补 `.xbf`/`.pri` → 校验 XAML 资源 → 编 `Boot.exe` 与
`DSH-Uninstall.exe` → 打 `payload.zip` → 拼成单个 `Setup.exe` → 自检 →
打印 SHA256 → 拷到桌面。

只在本地产出、不拷桌面:

```powershell
.\pack-release.ps1 -NoDesktop
```

---

## 二、正式发布:新 tag CI

```powershell
# 父级 set-version.ps1 已同步两仓版本
# 先审查、提交并推送本轮版本 / CHANGELOG / README / 交接
# 在安装器仓库创建并仅推送本次新 tag
git tag v1.5.4
git push origin v1.5.4
```

不要强推历史 tag。确认本仓库 CI 成功后，再推启动器同版本新 tag。正式产物及哈希均使用实际 CI Release 资产；本地验证包不能替代。

`v*` 的 tag 一推,`.github/workflows/release.yml` 就会:

1. 校验 **tag 和 `<InstallerVersion>` 一致**(对不上直接失败,免得发出去版本号是乱的);
2. 跑 `pack-release.ps1 -NoDesktop`;
3. **体积自检** —— 超过 40 MB 就报错(那说明自包含开关又被人打开了);
4. 算 SHA256,把 `DSH-Installer-Setup.exe` 传成 Release 资产。

也可以在 Actions 页面点 `workflow_dispatch` 手动跑(那种情况没有 tag,跳过第 1 步校验)。

---

## 三、历史手动打包示例（不用于当前正式发布）

以下 1.4.9 命令仅是历史构建说明；当前本地打包只用于验收，正式发布遵守顶部 tag CI 流程。

```powershell
# 1. 改版本号
#    Directory.Build.props -> <InstallerVersion>1.4.9</InstallerVersion>

# 2. 出包
.\pack-release.ps1 -NoDesktop
#    产物:dist\DSH-Installer-Setup.exe

# 3. 算哈希(发 Release 时写进去)
(Get-FileHash .\dist\DSH-Installer-Setup.exe -Algorithm SHA256).Hash.ToLower()

# 4. 建 tag 并推
git tag v1.4.9
git push origin v1.4.9
```

然后在 GitHub 上把 `dist\DSH-Installer-Setup.exe` 传成这个 tag 的 Release 资产。

---

## 四、发版前该过的几项

| 检查 | 为什么 |
|------|--------|
| `Directory.Build.props` 的版本号已改 | 界面、程序集、Release 全靠它 |
| `STATUS.md` 已更新 | 那是给下一个开发者的交接文档,不收工更新就废了 |
| 真机/虚拟机跑一遍**安装→卸载** | 有些毛病只有真装一遍才露头(本轮一半的修复都来自实测) |
| 卸载后没有残留目录 | 逐项删失败时会记进 `installer.log`,搜"删不掉" |
| 包体积还是个位数到十几 MB | 见 CI 里的体积自检 |
| `README.md` 里的说明还对得上 | 改过界面/开关的话 |

---

## 五、和启动器仓库的关系

安装器**不把启动器打进包**(那样每发一次启动器就得重发安装器)。
装的时候它去读启动器仓库根目录的 `manifest.json` 拿最新版 zip 地址 + sha256。

所以:

- **启动器发新版** → 当前更新协议要求两仓同版本同步发布，安装器即使无功能改动也需同步版本与新 tag CI。
- **清单 / API 和下载源均不可用** → 当前实现没有 `payload\launcher.zip` 离线回退，安装停止并提示错误，不保证断网安装。

---

## 六、常见问题

**Q:CI 上编译报找不到 dotnet?**
`pack-release.ps1` 优先用本机那套便携 SDK(`G:\DeepSeek DSH\.tools\dotnet`),
没有就退回 `PATH` 里的 `dotnet`。CI 上走的是后者,由 `setup-dotnet` 提供。

**Q:打出来的包一百多 MB?**
自包含开关被打开了。检查 `pack-release.ps1` 里是不是还写着
`--self-contained true` / `-p:WindowsAppSDKSelfContained=true` —— 那两个必须都是 false,
运行库交给引导程序按需装。

**Q:警告 `LF will be replaced by CRLF`?**
Windows 上的正常提醒,不影响。

**Q:Release 里的 SHA256 有什么用?**
给用户核对下载是否完整。将来若把安装器也接进自动更新,那份哈希就是校验依据。
