# 大肥鱼Go 1.4.9 过渡说明

安装器 `1.4.9` 负责先完成品牌迁移，暂不改变升级和卸载协议。

## 本版变更

- 产品显示名切换为中文“大肥鱼Go”、英文 `Dafeiyu-Go`。
- 安装器标题、欢迎页、完成页、卸载页和程序属性使用新品牌。
- 默认启动器仓库切换为：
  `YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run`
- 新仓库不可用时回退：
  `YunxiRamito/DSH-Launcher`
- 安装器版本和默认启动器基线同步为 `1.4.9`。
- 窗口材质切换为 Mica Alt，系统不支持时回退 Acrylic，再回退实色。
- 页面主题默认跟随 Windows 主机亮暗模式。

## 保持兼容的协议

- 安装包文件名 `DSH-Installer-Setup.exe`
- 卸载程序 `DSH-Uninstall.exe`
- 启动器文件名 `DeepSeek Harness.exe`
- 安装状态 `%LOCALAPPDATA%\DeepSeekHarness`
- 卸载注册表键 `Uninstall\DeepSeekHarness`
- 计划任务 `DeepSeekHarnessAutostart`
- 启动器目录名 `launcher`

卸载流程会同时清理新名称和旧名称的快捷方式。

## 发布顺序

1. 先完成启动器 `1.4.9` 的新仓库改名和发布。
2. 确认新启动器仓库的 `manifest.json` 可访问。
3. 构建并发布安装器 `1.4.9`。
4. 旧启动器仓库至少保留一个发布周期作为回退。

## 后续版本

`1.5.x` 之前继续沿用以上兼容协议。物理文件名、数据目录、注册表键和任务名若需要改变，
必须提供旧路径检测与迁移，不能只替换常量。
