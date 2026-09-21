# 大肥鱼Go安装器发布指南

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

## 二、CI 版:打 tag 自动出包

```powershell
# 先把版本号改好并提交
#   Directory.Build.props 里的 <InstallerVersion>
git add -A
git commit -m "release: v1.4.9"
git push

git tag v1.4.9
git push origin v1.4.9
```

`v*` 的 tag 一推,`.github/workflows/release.yml` 就会:

1. 校验 **tag 和 `<InstallerVersion>` 一致**(对不上直接失败,免得发出去版本号是乱的);
2. 跑 `pack-release.ps1 -NoDesktop`;
3. **体积自检** —— 超过 40 MB 就报错(那说明自包含开关又被人打开了);
4. 算 SHA256,把 `DSH-Installer-Setup.exe` 传成 Release 资产。

也可以在 Actions 页面点 `workflow_dispatch` 手动跑(那种情况没有 tag,跳过第 1 步校验)。

---

## 三、手动版:一步步来

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

- **启动器发新版** → 只推启动器仓库,安装器不用动;
- **清单拉不到**(断网之类)→ 回落到安装器自带的 `payload\launcher.zip`
  —— 注意那份**目前没打进正式包**(见 STATUS 的"收尾欠账"),断网时启动器是装不上的。

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
