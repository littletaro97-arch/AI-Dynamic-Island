# GitHub 发现率与展示优化：本地验收记录

## 基线与隔离

- 当前 GitHub 仓库：https://github.com/littletaro97-arch/AI-Dynamic-Island
- 克隆主分支基线：`7b70d0fefeee37f06d5d12e73c71241b0b3cc532`；独立本地分支：`codex/discovery-readme`。
- 全新网络克隆到本会话目录 `discovery-review`，未复制另一对话的未提交文件，未修改原工程、运行进程或其分支。
- 项目 Version、AssemblyVersion、FileVersion 为 0.8.0 / 0.8.0.0；API 最新正式 Release 为 v0.8.0；公开更新清单也是 v0.8.0。README 原为 v0.7.7。
- 已阅读此基线 AGENTS.md。没有 push、生成正式包 / Release、改版本、改更新协议或公开更新清单。

## 本轮改动

README 首页无版本项目名、中英文说明、下载入口、截图待办、精简功能、能力对照、安装 / 使用、数据及认证边界、限制与开发入口。移除过期的安装包完整版本名和发布目录名；保留必要的历史升级边界与历史发布说明链接。

新增真实截图清单、许可证 / 结构 / EXE 命名审查、元数据待应用方案、只读检索记录脚本和离线版本文档检查。没有运行时依赖、软件权限或逻辑改动。历史 HTML 和报告保持原位置。

## 能力证据与验证边界

| 文档事实 | 源码依据 |
| --- | --- |
| Codex 会话、额度、重置预告 | CodexStatusService、CodexSessionSource、CodexDecisionReader、CodexResetReminderTracker、MainWindow.UpdateCodexResetReminder |
| WorkBuddy 会话 / 宿主积分 | WorkBuddyStatusService、WorkBuddyCreditsService.ReadFromHostAsync |
| YOYO 任务、积分、可选认证签到 | YoyoStatusService、YoyoQuotaService、magicore-bridge/status.mjs、YoyoCheckinService.ReadSession / SendAsync |
| Harness 状态、完成 / 决策，无额度字段 | DeepSeekStatusService、harness-bridge/status.mjs |
| 通知按来源合并 | MainWindow.DetectCompletion / UpdateConfirmationNotice，以及系统通知服务 |
| 安装运行时与外部 SDK | Build-Release.ps1、主程序 csproj、安装器源文件 |

“已实现”表示代码路径存在，不表示本轮真实登录每个账户并验收通知。尤其完成排队、显示模式、第三方内部通道依赖实际环境，本轮未启动或操控桌面程序，不能宣称端到端通过。

## 验证结果

- 干净克隆 `dotnet build 'AI Dynamic Island.sln' -c Release`：失败，已有解决方案只列主项目，引用适配器构建落入 Debug，复制期寻找 Release DLL，错误 MSB3030。日志保留于本地 `artifacts/solution-build.log`。
- 显式构建适配器 Release，再直接构建主程序 csproj Release：通过，两项均 0 警告 / 0 错误；未改构建配置。README 使用经过验证的命令。日志为 `artifacts/adapter-build.log`、`artifacts/app-build.log`。
- `tools/Check-DiscoveryDocs.ps1`：通过，项目版本 / 清单 tag / 两个资产名与 URL 一致，README 无首页版本和过期包名，含 latest 下载入口。该检查不联网、不修改发布流程。
- Git diff 空白检查及 README 本地链接检查：通过（最终交付前执行）。
- 编译不等于实际桌面、账户、通知或 vendor SDK 验收；干净克隆缺少被忽略的 vendor SDK，未打包、未扫描既有 Release payload。

## GitHub 搜索方法

使用 GitHub CLI 调用 `GET /search/repositories`，未传 sort / order（默认 best match），`page=1`、`per_page=50`。六个关键词均分别使用原文本和附加 `in:readme`；不加引号，不加语言、账户或 star 过滤。每条保留精确 query、Asia/Shanghai 时间、total_count、incomplete_results、结果名称及目标位置。未命中只表示返回的前 50 项无目标，不是全站不存在。API 快照不同于浏览器登录态 UI 排名。

原始证据放在 `docs/discovery/before.json` 和 `after.json`。下方对照在两轮完成后生成。

**重要：after 是本地 README 整改后的公开仓库复测；公开 README、Description 和 Topics 仍未应用。它不是优化上线后的效果测量。** 上线后真实对照待所有者验收并授权应用 / push 后按同条件补测。索引延迟、排名波动与单次命中不能单独证明优化成功或失败，不承诺进入特定排名。

## 待验收 / 确认

1. 核对 README 功能与权限措辞，再决定是否应用 [Description / Topics 方案](DISCOVERY-METADATA.md)。
2. 提供并人工隐私复核 [2–3 张真实截图](assets/README.md)。
3. 确认第一方许可选择、Magicore vendor SDK 再分发与具体发行包第三方通知。
4. 如认可文档，再单独授权 push；不沿用其他对话的大更新或发布授权。
5. 公开修改实际生效后记录索引复测；目录整理与 EXE 改名仍仅为可选方案。

## 搜索前后对照（本地整改，公开内容未变更）

| 查询 | before 时间（+08:00） | before 位置 | after 时间（+08:00） | after 位置 |
| --- | --- | --- | --- | --- |
| AI Dynamic Island | 10/13/2026 23:42:12 | 36 | 10/13/2026 23:47:38 | 36 |
| AI Dynamic Island in:readme | 10/13/2026 23:42:21 | 前 50 未见 | 10/13/2026 23:47:47 | 前 50 未见 |
| codex dynamic island | 10/13/2026 23:42:31 | 33 | 10/13/2026 23:47:57 | 33 |
| codex dynamic island in:readme | 10/13/2026 23:42:40 | 前 50 未见 | 10/13/2026 23:48:06 | 前 50 未见 |
| agent quota monitor | 10/13/2026 23:42:50 | 前 50 未见 | 10/13/2026 23:48:16 | 前 50 未见 |
| agent quota monitor in:readme | 10/13/2026 23:42:59 | 前 50 未见 | 10/13/2026 23:48:25 | 前 50 未见 |
| codex usage widget windows | 10/13/2026 23:43:09 | 前 50 未见 | 10/13/2026 23:48:35 | 前 50 未见 |
| codex usage widget windows in:readme | 10/13/2026 23:43:19 | 前 50 未见 | 10/13/2026 23:48:44 | 前 50 未见 |
| AI agent status monitor | 10/13/2026 23:43:28 | 前 50 未见 | 10/13/2026 23:48:54 | 前 50 未见 |
| AI agent status monitor in:readme | 10/13/2026 23:43:39 | 前 50 未见 | 10/13/2026 23:49:03 | 前 50 未见 |
| windows desktop ai widget | 10/13/2026 23:43:48 | 前 50 未见 | 10/13/2026 23:49:13 | 前 50 未见 |
| windows desktop ai widget in:readme | 10/13/2026 23:43:58 | 前 50 未见 | 10/13/2026 23:49:22 | 前 50 未见 |

两轮均无 API 错误，incomplete_results 均为 false。此对照反映公开仓库在两次采样中的位置；真正上线后对照尚待授权发布与重新采样。
