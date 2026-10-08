<div align="center">

<img src="docs/readme/logo.svg" width="88" height="88" alt="Dafeiyu-Go 鲸鱼标">

# Dafeiyu-Go Setup

**把 DeepSeek Harness 的环境检测、组件安装和启动器部署交给一个向导。**

[在线交互预览](https://yunxiramito.github.io/Dafeiyu-Go/) · [下载安装包](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/latest) · [English](docs/README.en.md) · [配套启动器](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run) · [MIT](LICENSE)

<sub>One wizard to check prerequisites, install DeepSeek Harness and deploy its tray launcher.</sub>

<br>

[![installer release](https://img.shields.io/github/v/release/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup?label=installer&style=flat-square&color=111113)](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/latest)
[![launcher release](https://img.shields.io/github/v/release/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run?label=launcher&style=flat-square&color=111113)](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run/releases/latest)
![platform](https://img.shields.io/badge/Windows%2010%201809%2B%20%2F%2011-x64-0a84ff?style=flat-square)
![.NET 8](https://img.shields.io/badge/.NET-8-512bd4?style=flat-square)
![WinUI 3](https://img.shields.io/badge/WinUI-3-0a84ff?style=flat-square)
[![license](https://img.shields.io/github/license/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup?style=flat-square&color=6e7781)](LICENSE)

<br>

<a href="https://yunxiramito.github.io/Dafeiyu-Go/"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/readme/btn-preview-dark.png"><img src="docs/readme/btn-preview-light.png" height="44" alt="在线体验交互预览"></picture></a>&nbsp;&nbsp;<a href="https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/latest"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/readme/btn-download-dark.png"><img src="docs/readme/btn-download-light.png" height="44" alt="下载最新版安装器"></picture></a>

<sub>[Gitee 镜像](https://gitee.com/Yunxiramito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases) · [配套启动器](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run) · [English](docs/README.en.md) · [变更记录](docs/CHANGELOG.md)</sub>

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/hero-dark.webp">
  <img src="docs/readme/hero-light.webp" width="860" alt="Dafeiyu-Go 安装程序欢迎页">
</picture>

<sub>画面来自[在线交互预览](https://yunxiramito.github.io/Dafeiyu-Go/)（仅中文）。真实安装器支持中文与英文，可在右上角切换。</sub>

</div>

<br>

**1.6.0 已发布（2026-10-07）**：安装器与启动器从这一版起各自独立更新，互不卡住；启动器「更新」页新增「修复安装器」。这是两边最后一次同版发布。官方包未签名，见 [签名说明](SIGNING.md)。

## 在线体验

<table>
<tr>
<td width="50%" valign="top">

**[打开交互预览 →](https://yunxiramito.github.io/Dafeiyu-Go/)**

- 从欢迎页走到完成页，含取消与回滚
- 组件页的「选择 DSH 版本」高级入口
- 安装完成后启动器接手：检查更新、启动服务
- 随时切换亮色 / 暗色

</td>
<td width="50%" valign="top">

**它不会做什么**

- 不安装软件、不改 PATH、不写注册表
- 不发网络请求，路径选择不访问磁盘
- 导入用的是内置模拟 `.dym`
- 只有点「立即下载」才会打开 GitHub 发布页

</td>
</tr>
</table>

## 六步装好

<p align="center">
  <img src="docs/readme/setup-wizard.gif" width="900" alt="安装器从欢迎页依次经过下载源、安装范围、环境检测、DSH 目录、组件、推荐插件、启动器位置、确认、安装进度、导入备份和完成">
  <br><sub>准备 → 环境 → 选项 → 确认 → 安装 → 完成。录制自在线交互预览，全程模拟。</sub>
</p>

## 逐步导览

点开每一步查看界面与要点。截图会跟随 GitHub 的亮 / 暗主题。

<details>
<summary><strong>1 · 准备</strong>：欢迎与声明</summary>
<br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/step-1-welcome-dark.webp">
  <img src="docs/readme/step-1-welcome-light.webp" width="820" alt="欢迎页：在线安装、便携组件、系统支持说明">
</picture>

- 在线安装：DSH、启动器及缺失组件需要下载。
- Node / Git / pnpm / Python 使用便携版本，提供独立卸载程序。
- 已安装时，页脚「修复安装」可按安装记录修复。

</details>

<details>
<summary><strong>2 · 环境</strong>：下载源、安装范围与环境检测</summary>
<br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/step-2-detect-dark.webp">
  <img src="docs/readme/step-2-detect-light.webp" width="820" alt="环境检测：.NET 8 桌面运行时、Windows App Runtime 1.8、Node.js">
</picture>

- 下载源：大陆加速线路或官方线路，可配置代理。
- 安装范围：仅当前用户，或所有用户（需要管理员权限）。
- 检测 .NET 8 桌面运行时、Windows App Runtime 1.8 与 Node.js。

</details>

<details>
<summary><strong>3 · 选项</strong>：目录、组件、推荐插件与启动器</summary>
<br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/step-3-components-dark.webp">
  <img src="docs/readme/step-3-components-light.webp" width="820" alt="组件页：组件目录、强制重新下载、必需与可选组件">
</picture>

- 分别设置 DSH、便携组件与启动器目录。
- 按需选择 pnpm / Git / Python；符合要求的已有组件会复用，可勾选强制重新下载。
- 推荐插件可一并安装。
- 选择桌面快捷方式、开始菜单与登录自启。

</details>

<details>
<summary><strong>4 · 确认</strong>：安装计划</summary>
<br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/step-4-confirm-dark.webp">
  <img src="docs/readme/step-4-confirm-light.webp" width="820" alt="确认页：安装范围、下载来源、DSH 目录等只读摘要">
</picture>

- 只读汇总前面的选择：目标目录、组件安装或复用。
- 列出系统改动与需要提权的原因。
- 确认后点击「开始安装」。

</details>

<details>
<summary><strong>5 · 安装</strong>：进度与回滚</summary>
<br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/step-5-progress-dark.webp">
  <img src="docs/readme/step-5-progress-light.webp" width="820" alt="安装进度：当前阶段、百分比与步骤清单">
</picture>

- 按阶段显示进度与步骤清单。
- 安装中可以取消，取消会回滚本次改动。
- 失败页可导出安装日志。

</details>

<details>
<summary><strong>6 · 完成</strong>：导入备份并启动</summary>
<br>
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/readme/step-6-done-dark.webp">
  <img src="docs/readme/step-6-done-light.webp" width="820" alt="完成页：安装完成，勾选现在启动 Dafeiyu-Go">
</picture>

- 可导入以前的 `.dym` 备份，也可以直接跳过。
- 完成页可导出安装日志。
- 勾选「现在启动 Dafeiyu-Go」，由启动器接手。

</details>

## 装完之后

<p align="center">
  <img src="docs/readme/setup-handoff.gif" width="900" alt="点击完成后安装器关闭，任务栏出现鲸鱼图标，右下角信息窗口依次显示检查更新、下载、替换与 DSH 服务已启动">
  <br><sub>安装器关闭，启动器检查更新后拉起 DSH 服务。信息窗口的服务状态属于开发中的启动器 1.7.0（未发布）。</sub>
</p>

```mermaid
flowchart LR
    S["安装器<br/>DSH-Installer-Setup.exe"] -->|部署 DSH 与便携组件| D["DSH 本体<br/>Node.js 服务"]
    S -->|部署启动器| L["Dafeiyu-Go 启动器<br/>托盘 + 设置窗口"]
    L -->|启动 / 重启 / 停止| D
    L -->|读取发布清单，校验后自更新| L
    L -->|检查 / 修复| S
    U["DSH-Uninstall.exe"] -.->|卸载，默认保留用户数据| D
```

日常使用、插件、更新与备份都在启动器里完成，见 [Dafeiyu-Go 启动器](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run)。

## 快速开始

1. 在 [Releases](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases/latest) 下载 `DSH-Installer-Setup.exe` 并运行；国内也可从 [Gitee 发行版](https://gitee.com/Yunxiramito/Dafeiyu-Go-DeepSeek-Harness-Setup/releases) 下载。
2. 缺少运行库时，先确认下载与安装；按提示授予管理员权限。
3. 选择下载源、安装范围与目录，再按需选择 pnpm / Git / Python、推荐插件、快捷方式和自启。
4. 在确认页检查安装计划：目标目录、组件安装／复用、系统改动及提权原因；确认后点击「安装」。完成后通过启动器托盘或快捷方式打开 DSH。

已有且符合要求的组件会尽量复用；可选择强制重新下载。安装损坏时，可使用欢迎页的「修复安装」或 `--repair`。

## 安装前确认

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10 1809（build 17763）或更新版本，x64 |
| 网络 | **在线安装**；DSH、启动器及缺失组件需要下载 |
| Node.js | 最低 22.13.0；缺失时下载 Node 22 系列便携版 |
| 运行库 | .NET 8 桌面运行时、Windows App Runtime 1.8；缺失或版本不足时由引导程序补齐 |

> 当前源码版本为 **1.6.0**，实际下载版本以 Releases 为准。安装器仍为框架依赖的 WinUI 3 应用，不是自包含离线包。

**便携组件，不等于零系统改动。**

- DSH 与启动器部署到所选目录；新下载的 Node / pnpm / Git / Python 使用便携版，不走 Node 系统 MSI 安装。
- 安装器会按范围写入 PATH、卸载注册表入口与安装记录，并按选项创建快捷方式、自启计划任务。
- 两个运行库是系统级安装。选择「所有用户」、启用自启、补装运行库或写入受保护目录时，需要管理员权限；启动器自身也可能触发 UAC。**不保证全程只弹一次。**
- 启动器在线读取发布清单，必要时尝试旧仓库及 GitHub API；加速线路还可从 npm 镜像下载。当前安装流程**没有内置 `payload\launcher.zip` 离线回退**，已有 Node 和运行库也不代表可以离线安装。

默认当前用户目录为 `%LOCALAPPDATA%\DeepSeek Harness`；所有用户目录为 `%ProgramFiles%\DeepSeek Harness`。兼容文件名和数据目录沿用旧协议，见 [过渡说明](TRANSITION.md)。

## 卸载与排障

在 Windows「应用和功能」中卸载，或运行安装目录中的 `DSH-Uninstall.exe`。卸载默认保留设置、技能、插件与会话数据；取消保留前请先备份。外部复用的组件不是本安装器下载的便携组件，两个系统运行库也不会随本体目录一起移除。

失败页和完成页可**导出安装日志**。分享前仍应检查是否包含私人路径或其他敏感信息，不要直接上传配置、凭据或完整状态文件。

| 位置 | 用途 |
| --- | --- |
| `%LOCALAPPDATA%\DeepSeekHarness\installer.log` | 安装日志 |
| `%LOCALAPPDATA%\DeepSeekHarness\installer-state.json` | 本机安装记录，排查路径或修复时参考 |
| `%TEMP%\dsh-boot.log` | 引导程序与运行库安装日志 |

下载失败时切换向导中的下载源，或检查代理设置；运行库安装失败时可手动安装后重试。

**官方构建目前未签名。** SmartScreen 警告不能证明文件安全或有害；先核对下载来源。确认来自本仓库 Releases 后，可选择「更多信息 → 仍要运行」，或在文件属性中「解除锁定」。详见 [签名说明](SIGNING.md)。

<details>
<summary><strong>命令行与无人值守安装</strong></summary>

发布安装包接受参数并转交主程序。`--silent` 跳过向导，但不会绕过 UAC；批量部署时应预先准备相应权限。

```powershell
# 当前用户安装，不注册自启，安装后不启动
.\DSH-Installer-Setup.exe --silent --scope=user --source=china --pnpm --git --no-autostart --no-launch --report=install.json

# 按已有安装记录进入修复流程；无记录时退回普通安装
.\DSH-Installer-Setup.exe --repair

# 预览页面（0 起编号）；仍可能写入日志、缓存与配置
.\DSH-Installer-Setup.exe --page=4
```

| 参数 | 用途 |
| --- | --- |
| `--silent` / `--uninstall` / `--repair` | 无向导安装 / 卸载 / 修复 |
| `--scope=user\|machine` | 当前用户 / 所有用户 |
| `--source=china\|official` | 加速线路 / 官方线路 |
| `--dsh-root=<路径>` | DSH 目录 |
| `--launcher-root=<路径>` | 启动器目录 |
| `--components-root=<路径>` | 便携组件目录 |
| `--pnpm` / `--git` / `--python` | 选择可选组件 |
| `--force-reinstall` | 对所选组件禁用复用，重新下载便携版 |
| `--no-shortcut` / `--no-autostart` / `--no-launch` | 不建桌面快捷方式 / 不注册自启 / 不在完成后启动 |
| `--no-runtime` | 跳过主安装流程的运行库步骤；**不禁用外层引导程序的运行库补装** |
| `--report=<文件>` | 写入 JSON 结果 |
| `--dry-run` | 演练主要安装步骤；**不是零写盘或隔离沙箱** |
| `--page=N` / `--lang=zh\|en` | 跳页预览 / 界面语言 |

`--page=N` 默认启用演练；同时传 `--install` 或 `--silent` 则允许真实安装。外层引导程序仍会解压安装器、记录日志并按需补装运行库；不要在未准备好的机器上把预览当作无副作用测试。

</details>

<details>
<summary><strong>从源码构建</strong></summary>

使用 Windows x64、.NET 8 SDK 与可用的 Windows SDK 构建环境。Windows App SDK `1.8.260804001` 由 NuGet 恢复；打包脚本还调用系统 .NET Framework 的 `csc.exe`。

```powershell
# 共享库
dotnet build src\DshInstaller.Shared\DshInstaller.Shared.csproj -c Release

# WinUI 主程序
dotnet build src\DshInstaller\DshInstaller.csproj -c Release

# 开发预览包
.\pack-preview.ps1

# 正式安装包：dist\DSH-Installer-Setup.exe，不复制到桌面
.\pack-release.ps1 -NoDesktop
```

打包脚本会清理输出并停止匹配的安装器进程，请先结束本机测试。DSH 安装范围为 `@deepseek-ai/dsh@^0.1.5-rc.1`，不是锁定的精确版本；版本与系统门槛参考 [构建配置](Directory.Build.props)。

维护入口：[交接说明](HANDOVER.md) · [1.5.0 功能交接](HANDOVER-1.5.0-installer.md) · [项目状态](STATUS.md) · [发布流程](RELEASE.md)。历史文档与实现不一致时，以当前源码和发布产物为准。

</details>

## 相关项目

| | |
| :--- | :--- |
| [**Dafeiyu-Go 启动器**](https://github.com/YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run) | 托盘启动器：余额与用量、插件与技能、更新与备份 |
| [**在线交互预览**](https://yunxiramito.github.io/Dafeiyu-Go/) | 浏览器里体验安装、更新与全部设置分组，全程模拟 |
| [**DeepSeek Harness**](https://github.com/deepseek-ai/deepseek-harness) | 安装器部署的本机服务 |

---

[MIT License](LICENSE) · [English](docs/README.en.md) · [签名说明](SIGNING.md) · [过渡说明](TRANSITION.md)
