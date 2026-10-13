# AI Dynamic Island

Windows AI 状态灵动岛：集中查看 **Codex、WorkBuddy、YOYO Claw、DeepSeek Harness 的任务状态**，并为部分应用显示额度、积分与提醒。

A Windows desktop widget and AI agent status monitor for Codex, WorkBuddy, YOYO Claw and DeepSeek Harness, with usage quota monitoring for supported agents. Use it as a Codex usage widget alongside multi-agent task tracking and notifications.

**[下载最新正式版 → Releases/latest](https://github.com/littletaro97-arch/AI-Dynamic-Island/releases/latest)** · Windows 10/11 x64 · 安装版 / 便携版 · 内含 .NET 8 Desktop Runtime

它将额度查看、多应用任务状态、最近回复与提醒放在常驻桌面的紧凑界面中，适合不想反复切换 Agent 窗口的用户。额度并非覆盖所有 Agent，具体见下表。对应 Agent 需已安装并登录；YOYO 任务桥接还需要 Node.js，DeepSeek Harness 使用其自带运行时。

## 界面展示

真实运行截图待补充：收起状态、展开状态及可选的任务通知。当前没有经过来源与隐私核验的截图，不使用原型或生成图代替软件界面。见 [截图清单](docs/assets/README.md)。

## 核心功能

- 收起时常驻桌面，悬停或点击展开查看多 Agent 状态与最近回复。
- Codex 额度与重置时间、WorkBuddy 和 YOYO Claw 积分；来源不可用时显示不可用或未知。
- 任务完成提醒，Codex / WorkBuddy / DeepSeek Harness 待回答或待确认提醒，以及 Codex 额度重置预告。
- 多显示器与 DPI 定位、主题、字号、透明度、滚动、来源顺序、托盘和应用内更新。
- 可选 Windows 通知展示；可选适配已安装的 Magic 视界顶部栏，复用已有状态来源。

## 支持应用与能力

以下为当前源码中已实现的能力及依赖条件，**不是本轮逐服务实机验收结果**。第三方升级、权限或数据缺失可能使功能不可用。“未实现”不表示服务本身没有该能力。

| 应用 | 任务状态 | 额度 / usage quota | 额度重置提醒 | 应用内任务通知 | 条件 |
| --- | --- | --- | --- | --- | --- |
| Codex | 执行、完成、待回答/审批 | 已实现：用量窗口与重置时间 | 已实现：重置预告 | 已实现：完成、待回答/审批 | 本机会话与 Codex app-server，额度需有效登录 |
| WorkBuddy | 执行、完成、待确认 | 已实现：积分 | 未实现 | 已实现：完成、待确认 | 本机会话；积分需宿主 IPC 可连接且权限匹配 |
| YOYO Claw | Magicore 任务状态、完成 | 已实现：积分批次与账单余额 | 未实现 | 已实现：完成；可选签到结果 | 本机 YOYO / Magicore 可用；任务桥接需 Node.js 与 vendor SDK |
| DeepSeek Harness | 执行、完成、异常、待决策 | 未实现 | 未实现 | 已实现：完成、待决策 | 本机 Harness 会话及其自带运行时 |

任务通知在灵动岛内呈现，不代表各 Agent 都向 Windows 通知中心发送通知。另一个可选功能是展示 Windows 已有 Toast；需要通知访问权限，无法捕获未提交到通知中心的普通弹窗。

## 下载与安装

打开 **[最新正式 Release](https://github.com/littletaro97-arch/AI-Dynamic-Island/releases/latest)**，在 Assets 中选择：

| 下载类型 | 使用方式 | 卸载方式 |
| --- | --- | --- |
| Windows x64 安装包（以 `-win-x64-setup.exe` 结尾） | 按向导安装到当前用户目录，可创建快捷方式 | Windows 设置 → 应用 → AI Dynamic Island，或开始菜单卸载入口 |
| Windows x64 便携包（以 `-win-x64-portable.zip` 结尾） | 解压完整目录，运行 `YoyoClawCompanion.exe` | 关闭开机启动并从托盘退出，再删除解压目录 |

两种包均包含 .NET 8 Desktop Runtime，无需额外安装 .NET。缺少某个 Agent 不影响其他来源。YOYO 任务桥接需要另行安装 [Node.js](https://nodejs.org/)；仅使用其他来源无需为 YOYO 安装 Node.js。Magic 视界适配是可选项，普通灵动岛无需安装 Magic 视界。

升级前从托盘退出旧版本。安装版按向导升级；便携版解压到新目录运行。配置保存在 `%APPDATA%\YoyoClawCompanion`，便携版迁移到安装版会沿用配置。卸载默认保留配置与诊断日志，确认不再需要后可手动删除数据目录。

## 使用说明

1. 正常启动并登录要监控的 Agent，然后运行灵动岛。
2. 从托盘或再次启动程序进入设置，选择显示来源、主题、位置和提醒。
3. 按设置悬停或点击展开；反向选择与悬停展开同时启用时，按住 Alt 进行悬停或点击展开。
4. 来源或积分不可用时先检查对应应用是否运行、登录及权限，必要时“重置并重新检测”。未知不等于零额度。
5. 在设置 → 更新中检查新版本，查看下载进度与失败原因。

更新使用本仓库最新正式 Release，草稿与预发布不参与；GitHub API 限流时回退到 `docs/update.json`。下载后校验 SHA-256，安装版打开向导，便携版等待旧进程退出后替换并重启。老旧 v0.6.0 用户需从 Releases 手动升级；错误日志位于 `%LOCALAPPDATA%\YoyoClawCompanion\update.log`。

## 数据来源、认证与隐私

**会读取本地任务数据；部分功能会使用本机已有认证信息。** 本项目是非官方工具，不是各服务的统一官方监控接口。

| 来源 / 功能 | 读取与认证范围 |
| --- | --- |
| Codex | 本地会话生命周期、回复与决策事件；通过本机 app-server 请求额度。支持 `CODEX_HOME`，默认 `.codex`。主程序不直接读取或保存 Codex 认证文件，app-server 使用其自身登录状态。 |
| WorkBuddy | 本地会话与回复；读取 `wbipc/endpoint.json` 的端点与 ticket，在本机命名管道认证后经宿主请求积分。支持 `WORKBUDDY_CONFIG_DIR`，默认 `.workbuddy`。 |
| YOYO 状态 / 积分 | 桥接读取 `.magicore/data/control-grpc/bearer_token` 连接本机服务；积分读取 `%APPDATA%/hclaw/billing/quota.json` 与本机 YOYO 账单日志。 |
| YOYO 可选自动签到 | 开启后读取 YOYO 的 `Local State` 与 `billing/session.bin`，使用当前用户的 Windows 解密能力恢复会话，携带 access token 与设备指纹请求荣耀签到服务。 |
| DeepSeek Harness | 读取 `.dsh/sessions`，通过 Harness 自带运行时解析任务事件与回复；没有额度接口。 |
| Windows 通知 / 更新 | 通知需 Windows 通知访问权限，可能含私人消息；更新连接 GitHub，不上传用户会话用于更新检查。 |

回复会在桌面呈现，共享屏幕或截图前请关闭敏感展示。诊断日志与截图分享前检查用户名、路径、会话内容、账户标识及凭据。可选签到向服务端发送认证信息，WorkBuddy 宿主积分请求也可能联网；读取本地数据不等于所有功能完全离线。

YOYO 自动签到移植自 `YOYOClawCheckin v1.3`，仅限本人账号与有合法使用权的电脑；禁止代签、批量签到、规避限制及商业用途。见 [原许可](THIRD_PARTY_NOTICES/YOYOClawCheckin-LICENSE.txt)。

## 已知限制

- 状态来自本地记录或内部接口推断，不提供虚构进度百分比；第三方升级可能改变格式与认证通道。
- 独立 ChatGPT 网页没有连接到本应用的 Codex 状态来源；DeepSeek Harness 没有额度及重置提醒实现。
- 缺少 SDK、运行时、有效登录或文件 / IPC 权限时，相应能力可能不可用。
- 全屏检测按窗口覆盖显示器判断，全屏视频与演示也可能触发隐藏。混合 DPI、多屏及不同 Magic 视界版本需实机核验。
- 缺少顶层 LICENSE；第一方许可选择与 Magicore vendor 再分发授权待确认。**不能将本仓库视为已获 MIT、Apache-2.0 或 GPL 授权。** 见 [许可证与结构审查](docs/LICENSING-AND-STRUCTURE-REVIEW.md)。

## 开发、构建与贡献

开发需要 .NET 8 SDK；适配器原生 DLL 需要 MSVC x64 工具。安装 EXE 打包另需 Inno Setup 6。干净克隆建议直接构建项目，确保适配器采用同一配置：

```powershell
dotnet build .\MagicStatusbarAdapter\MagicStatusbarAdapter.csproj -c Release
dotnet build .\YoyoClawCompanion\YoyoClawCompanion.csproj -c Release
```

版本以 `YoyoClawCompanion/YoyoClawCompanion.csproj` 为准。`Build-Release.ps1` 生成安装与便携包，输出到 `artifacts` 及按项目版本命名的 publish 目录；发布前遵守 [AGENTS.md](AGENTS.md) 的验收与授权要求。

`@magicore/*` 来自本机 YOYO 安装包，是 vendor 组件，不是普通 npm 可还原依赖。不要运行 `npm ci` 来替代 SDK；缺少 SDK 时发布会明确失败，构建成功不等于该来源能运行。再分发需单独核对授权。

贡献前阅读协作要求与架构，提交变更范围及验证证据；涉及发布、协议或权限的修改需说明兼容风险。历史报告和 HTML 原型保留在原位置，不代表当前界面或完整实机验收。

| 路径 / 文档 | 用途 |
| --- | --- |
| `YoyoClawCompanion/` | WPF 主程序、状态来源、配置、提醒与更新 |
| `MagicStatusbarAdapter/` | Magic 视界显示适配器与原生布局探测 |
| `magicore-bridge/` / `harness-bridge/` | YOYO 与 DeepSeek 状态桥接 |
| `integrations/workbuddy/` | WorkBuddy 完成信号辅助脚本 |
| `installer/` / `Build-Release.ps1` | 安装源文件与既有打包流程 |
| [架构](docs/ARCHITECTURE.md) / [通知设计](docs/NOTIFICATION-BATCHES-2026-10-06.md) | 维护背景与实现边界 |
| [历史发布说明](docs/RELEASE-v0.7.7.md) / [正式 Releases](https://github.com/littletaro97-arch/AI-Dynamic-Island/releases) | 历史变更与当前发布信息 |
| [截图清单](docs/assets/README.md) / [展示优化验证](docs/DISCOVERY-REVIEW.md) | 展示资源待办、检索条件与验收边界 |

隐藏 YOYO 官方悬浮球时优先使用官方托盘菜单。辅助脚本需先退出 YOYO 并备份设置；用 `scripts/Restore-OfficialFloatingBall.ps1` 恢复。
