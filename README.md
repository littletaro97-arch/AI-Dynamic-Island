# AI 灵动岛（YOYO Claw Companion）

独立、只读的 YOYO Claw 伴生悬浮球。它不修改 `app.asar`，不写入积分或任务数据库。

## 当前功能

- 读取 `%APPDATA%\hclaw\billing\quota.json` 显示剩余积分。
- 通过 YOYO 自带的 Magicore SDK 只读调用 `conversation.list_views` 和 `conversation.thread_view`，显示当前账号的空闲/忙碌与最近任务结果。
- 折叠态显示 YOYO、Codex、WorkBuddy 三个状态灯，悬停展开为多来源状态面板。
- Codex 结合本地会话生命周期区分“空闲/执行中”，并通过 Codex app-server 的只读接口显示 5 小时与周限额。
- WorkBuddy 只读解析 `%USERPROFILE%\.workbuddy\projects` 的最近会话流水，并通过宿主 `wb.request` 通道显示真实剩余积分。
- 单击每个状态点或展开后的整行可打开/激活对应应用；长按拖动可改变位置，右键可打开主页或退出。
- 支持跟随系统、浅色、深色三种主题；悬停延时、回弹动效、点击启动及各数据组件均可在主页独立开关。
- YOYO Claw、Codex 或 WorkBuddy 产生新的最终回复后，灵动岛默认展开 10 秒并用三行区域显示来源与回复摘要；展开时间可调，首次启动只建立基线，不弹出历史完成记录。
- “全部就绪”摘要按 `YOYO Claw | Codex | WorkBuddy` 顺序滚动显示三方余额；Codex 优先显示 5 小时额度，没有时再显示周额度，滚动速度可调。
- 状态色：绿色空闲、黄色忙碌、红色错误或最近失败、灰色未运行；三方额度统一使用积分强调色。
- 可切换为“仅任务活动时显示”：全部空闲时隐藏，执行任务时显示折叠态，完成时显示展开态。隐藏期间再次启动程序会直接打开设置主页。
- 拖动接近屏幕水平中心时会小范围吸附；允许放置在屏幕底部，靠近任务栏时自动改为向上展开。
- 可选择是否显示系统托盘图标；双击托盘图标打开主页，右键可刷新状态或退出。
- 折叠态余额以可调速度从右向左匀速循环，不会因定时状态刷新而在 Codex 中途重新开始。
- 灵动岛文本统一使用半粗体；字号可实时调整，字号过大无法容纳两行时折叠态自动切换为单行。
- 回复区域可设置最多显示 1–6 行，展开高度会随字号与最大行数自动调整。

## 构建与运行

```powershell
dotnet build .\YoyoClawCompanion.sln -c Release
dotnet run --project .\YoyoClawCompanion\YoyoClawCompanion.csproj -c Release
```

也可以运行 `Build-Release.ps1` 生成 `publish-v0.5\YoyoClawCompanion.exe`，之后通过
`Start-YoyoClawCompanion.ps1` 启动。

## 已知边界

- 当前版本依据各应用的任务生命周期判断忙碌，不把“应用进程存在”误报为“正在执行任务”，也不伪造百分比进度。
- 浅色和深色模式下，组件悬停统一使用随外框圆角变化的灰色描边，不再使用会遮挡文字的系统蓝色按钮背景。
- 圆角、不透明度、宽度和字号属于纯外观更新，不会触发三方重新取数或短暂覆盖状态颜色；折叠宽度会即时生效。
- 仅读取当前列出的 Agent 与活动会话，并按真实会话更新时间排序；不再读取旧的 `host_runtime.db`。
- Magicore 接口不可访问时会明确显示“任务状态不可用”，不会静默回退到历史数据库。
- YOYO 刷新禁止异步重入；接口异常会明确降级，不把旧成功快照伪装成当前在线状态。诊断信息写入 `%LOCALAPPDATA%\YoyoClawCompanion\diagnostics.log`。
- 最近任务只有在 30 分钟内确实失败时才显示红色，历史错误不会让状态灯永久告警。
- Codex 限额使用本机已登录 Codex 的 app-server，不读取或保存认证文件；接口不可用时只降级该项显示。
- WorkBuddy 积分通过带双向校验的本机命名管道读取，票据只在内存中用于握手，不写入日志或设置文件。
- WorkBuddy 的 JSONL 与宿主 IPC 都属于内部接口，升级后如果结构变化，只影响 WorkBuddy 一行，不会拖垮 YOYO 或 Codex 状态。
- 应用启动路径不绑定安装盘：优先记忆运行中进程的真实路径，再查询 Windows `App Paths`、卸载信息和常见安装目录。Codex 商店版还可通过 AppsFolder 激活。
- WorkBuddy 数据目录支持 `WORKBUDDY_CONFIG_DIR`，Codex 会话目录支持 `CODEX_HOME`；未配置时分别使用当前用户的 `%USERPROFILE%\.workbuddy` 与 `%USERPROFILE%\.codex`。
- 完全未注册且从未运行过的任意目录便携版无法由 Windows 全盘无损定位；先运行一次应用，灵动岛会自动记录其真实路径，之后关闭状态下也能重新打开。
- 任务桥接依赖本机 Node.js；发布目录内包含从当前 YOYO 安装包提取的只读 SDK 运行组件。
- `@magicore/*` 是随 YOYO 版本提取的 vendor 依赖，不要在 `magicore-bridge` 内运行 `npm ci`；构建脚本会在依赖缺失时中止并给出明确错误。
- 程序具有单实例保护；再次启动不会生成重叠的第二个灵动岛，而是唤出已有实例的设置主页。
- 官方悬浮球需从 YOYO Claw 托盘菜单正常关闭；本项目不修改官方 ASAR。

## 隐藏官方悬浮球

首选方式是在 YOYO Claw 托盘菜单中选择“隐藏悬浮球”。如果需要持久化处理：

1. 从托盘正常退出 YOYO Claw。
2. 运行 `scripts\Hide-OfficialFloatingBall.ps1`。
3. 重新启动 YOYO Claw。

脚本会先备份官方 `state.json`，检测到 YOYO 仍在运行时会拒绝写入。使用
`scripts\Restore-OfficialFloatingBall.ps1` 可恢复显示。
