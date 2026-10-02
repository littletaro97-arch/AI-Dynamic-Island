# AI Dynamic Island v0.7.2

Windows 桌面上的 AI 状态灵动岛，集中显示 Codex、WorkBuddy 和 YOYO Claw 的任务状态、额度及最近回复。支持悬停展开、多显示器、主题、托盘、完成提醒和应用内更新。

## 下载与安装

在 [Releases](https://github.com/littletaro97-arch/AI-Dynamic-Island/releases/latest) 中选择：

| 下载包 | 使用方式 | 卸载方式 |
| --- | --- | --- |
| `AI-Dynamic-Island-v0.7.2-win-x64-setup.exe` | 按向导安装到当前用户目录，可创建快捷方式 | Windows 设置 → 应用 → 已安装的应用 → AI Dynamic Island，或开始菜单卸载入口 |
| `AI-Dynamic-Island-v0.7.2-win-x64-portable.zip` | 解压后运行 `YoyoClawCompanion.exe`，保留完整目录 | 从托盘退出后删除解压目录；如开启过开机启动，删除前先关闭此设置 |

适用于 Windows 10/11 x64。两种包均内含 .NET 8 Desktop Runtime。YOYO 任务桥接需要另行安装 [Node.js](https://nodejs.org/)，对应功能还需要本机安装并登录相应 AI 应用。缺少某个来源时不影响其他来源。

首次使用可从托盘或再次启动程序进入设置，选择显示来源、主题和位置。应用可按任务活动自动显示，也可在全屏时临时隐藏空闲状态。

升级前请先从托盘退出旧版本。安装版按向导升级，便携版解压到新目录运行。配置统一保存在 `%APPDATA%\YoyoClawCompanion`，与程序目录分离；从便携版迁移到安装版时会沿用配置。卸载默认保留配置与诊断日志，确认不再需要后可手动删除此数据目录。

## 核心功能

- Codex：读取本地会话生命周期和最终回复，通过本机 app-server 获取额度与重置时间。
- WorkBuddy：读取本地会话，识别执行、完成与待确认；通过本机宿主通道获取真实积分。
- YOYO Claw：读取有效积分批次及较新的官方账单记录、Magicore 任务状态，可选启用联网签到。
- 可调字号、折叠与展开宽度、圆角、透明度、滚动速度和程序顺序。
- 完成提醒、待确认提醒、额度重置提醒、状态长时间不变自动隐藏。
- 多显示器与 DPI 支持、边缘展开、屏幕中心吸附、托盘和设置预设。
- 设置 → 更新：自动检查或手动检查，显示版本、下载进度和失败原因；下载后校验 GitHub 提供的 SHA-256。

## 更新说明

更新来源为本仓库最新正式 Release，预发布和草稿不参与更新。GitHub API 限流时改用仓库中的公开版本清单 `docs/update.json`；发布维护者需在两个资产上传并校验后同步清单。安装版下载对应安装 EXE 并打开向导；便携版下载 ZIP，等待旧进程退出后替换文件并重新启动。

v0.6.0 的更新代码使用固定 ZIP 名称，与实际 Release 文件不一致。因此已有 v0.6.0 用户首次升级到 v0.7.1 需要从 Releases 手动下载。v0.7.1 开始使用一致的版本化文件名。更新失败时查看设置中的提示及 `%LOCALAPPDATA%\YoyoClawCompanion\update.log`。

## 项目结构

```text
AI Dynamic Island.sln       WPF 解决方案
YoyoClawCompanion/          .NET 8 Windows 主程序
  MainWindow.xaml[.cs]      灵动岛显示、布局、状态与提醒
  SettingsWindow.xaml[.cs]  设置主页与更新界面
  App.xaml[.cs]             单实例、托盘、启动与异常记录
  Services/                状态来源、配置、窗口、更新与启动注册
magicore-bridge/            YOYO Magicore 只读桥接
integrations/workbuddy/     WorkBuddy 完成信号辅助脚本
installer/                 Inno Setup 安装/卸载向导源文件
scripts/                   官方悬浮球设置备份与恢复
THIRD_PARTY_NOTICES/        第三方许可
Build-Release.ps1          生成两个 Windows x64 发布包
Start-YoyoClawCompanion.ps1 本地启动发布程序
docs/                      发布说明、验证记录与架构说明
```

## 开发与打包

开发需要 .NET 8 SDK。安装 EXE 的编译另需 Inno Setup 6。

```powershell
dotnet build '.\AI Dynamic Island.sln' -c Release
.\Build-Release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
# 仅生成便携版
.\Build-Release.ps1 -PortableOnly
```

输出目录为 `artifacts`，应用发布目录为 `publish-v0.7.2`。版本号以 `YoyoClawCompanion.csproj` 为准。

Magicore 的 `@magicore/*` 是从本机 YOYO 安装包取得的 vendor 运行组件，不属于普通 npm 可还原依赖。不要在桥接目录运行 `npm ci`；缺少 SDK 时打包会明确失败。第三方运行组件的再分发应遵循其授权。

## 数据与使用边界

状态依据本地应用数据或接口推断，不代表官方通用进度 API，也不提供虚构百分比。独立 ChatGPT 网页没有连接到本应用的 Codex 状态来源。全屏检测按窗口覆盖显示器判断，因此全屏视频和演示也会触发。

Codex 支持 `CODEX_HOME`，WorkBuddy 支持 `WORKBUDDY_CONFIG_DIR`。更新检查会连接 GitHub，YOYO 签到会连接对应服务；本项目不上传用户会话用于更新检查，不读取或保存 Codex 认证文件。内部接口可能随第三方应用升级而改变。

YOYO Claw 自动签到功能移植自 `YOYOClawCheckin v1.3`，仅供本人账号在本人有权使用的电脑上使用；禁止代签、批量签到、规避服务限制及商业用途。完整许可见 [第三方许可](THIRD_PARTY_NOTICES/YOYOClawCheckin-LICENSE.txt)。项目没有授予超出第三方许可的使用权。

如需隐藏 YOYO 官方悬浮球，优先使用官方托盘菜单。辅助脚本需先正常退出 YOYO，并会备份其设置；使用 `scripts\Restore-OfficialFloatingBall.ps1` 恢复。
