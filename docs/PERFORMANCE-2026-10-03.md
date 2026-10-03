# 资源优化与验证记录（2026-10-03）

本轮在 `66d7f34` 基线上完成第一阶段优化，代码提交为 `be3207b`，分支为 `codex/performance-incremental-monitoring`。应用版本仍为 0.7.2；只做本地源码、测试和 Git 提交，没有生成正式安装包或便携包，没有推送或修改 GitHub Release。当前安装版没有被替换。

代码回滚可使用 `git revert be3207b`，无需删除用户文件。验证构建位于 `.build-check/performance-preview`，真实桌面验收及正式发行仍待后续要求。

## 本轮修改

1. **额度读取子进程清理**：Codex RPC 读取完成后先关闭输入，等待正常退出；超时再通过私有 Windows Job Object 清理本应用刚创建的子进程。替换 `Process.Kill(true)` 的全系统后代枚举路径。创建失败时不发送 RPC 工作，只清理自己的根进程。额度刷新频率保持一分钟。
2. **检测共享快照**：每次刷新用 Toolhelp 获取一份进程名称/ID 快照，四个来源共享运行判断。只为需要路径、窗口标题判断的候选进程打开额外句柄，避免每个来源重复枚举。
3. **目录与日志增量读取**：Codex、WorkBuddy、DeepSeek 使用有界最近会话索引。文件监视器提供变更提示，候选文件仍检查元数据；一分钟全量目录复核，并在监视器报错、队列溢出、重命名时重建。Codex、WorkBuddy 的 JSONL 游标只解析新增记录，保留跨次工具调用和完成状态。
4. **减少无效 UI 工作**：状态文字复用 `Run`，值和颜色未变时不重建 Inlines。方案名称改用 WPF 原生动画时钟，在滚出视口、隐藏或最小化后停止，重新显示后恢复，两份滚动副本同步。
5. **滚动文字与阴影分离**：把阴影放入独立缓存层，裁掉岛内部，移动文字不会让整个带阴影的岛重新计算效果。仍保留阴影及正常动画；开关支持快速反向切换。

Job Object 的限制仅用于本应用创建的额度读取进程，相关行为依据 [Microsoft Job Objects 文档](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)。共享进程快照使用 [Microsoft Toolhelp 示例中的接口](https://learn.microsoft.com/en-us/windows/win32/toolhelp/taking-a-snapshot-and-viewing-processes)。

## 对比方法与结果

使用 Release 构建比较基线 DLL 和修改后的 DLL，测试宿主相同。CPU 按进程 CPU 时间增量除以墙钟时间及 16 个逻辑处理器归一化；内存分别记录工作集、私有提交和 GC 堆，不能混用。

界面场景在 200% 缩放、WPF Render Tier 2 下，预热约 3.5 秒后采样 15 秒。表中的滚动文字为 80 个重复汉字，关闭检测轮询、启动注册、托盘和更新副作用，以隔离渲染开销。它是可重复的控件负载，不代表完整应用的日常平均占用，也不是显示器帧率测量。

| 长文字滚动场景 | 基线 | 修改后 |
| --- | ---: | ---: |
| 第一次平均 CPU | 2.785% | 1.639% |
| 第二次平均 CPU | 2.713% | 1.490% |
| 两次平均 CPU | 2.749% | 1.564% |
| 两次平均私有提交 | 275.5 MiB | 250.1 MiB |
| 两次平均工作集 | 334.6 MiB | 309.8 MiB |

该场景 CPU 下降约 43%，私有提交下降约 25.4 MiB。折叠场景约 0.09% CPU，改善主要集中在文字持续滚动时。

| 检测轮询比较：预热 5 次后连续 50 次 | 基线 | 修改后 |
| --- | ---: | ---: |
| 每次平均墙钟耗时 | 14.98 ms | 11.13 ms |
| 50 次进程 CPU 时间 | 1.422 秒 | 0.656 秒 |
| 每次托管分配 | 441,001 B | 86,158 B |

上述轮询使用本机真实 Codex、WorkBuddy 会话，不含额度 RPC 或 UI。期间会话仍可能变化，因此是当前数据规模下的观测，不能推导出所有机器或端到端应用都降低相同比例。

额度读取另测三次：基线成功读取三次，但记录到 **2,330 次 Win32 异常**，首个调用栈指向 `Process.Kill(true)` 的进程树枚举。修改后三次均成功，记录到 **0 次 Win32 异常**。网络请求时长存在波动，本轮不以请求延迟证明清理优化。

方案名称动画改动前后的独立场景中，长名称位于屏幕外时 CPU 约 0.159% → 0.071%；可见时约 1.009% → 0.814%。可见场景私有提交没有下降，所以不能把动画时钟改动描述为内存优化。

## 内存判断与剩余问题

使用 `VirtualQueryEx` 与 `QueryWorkingSetEx` 读取进程区域类别及驻留情况，不读取进程内容或生成转储。相关字段依据 [VirtualQueryEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex) 和 [QueryWorkingSetEx](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-queryworkingsetex) 文档。

全新 Release 折叠宿主 GC 堆约 4.3 MiB，设置宿主约 6.2 MiB；其私有驻留区域约 138 MiB / 227 MiB，另外还有映像和映射区域。这说明大量内存不在托管 GC 堆里，但区域类型无法说明具体是谁分配的，更不能单凭它判定 WPF、驱动或业务代码泄漏。

安装版的单次观测因生命周期、设置和源码基线与全新测试宿主不同，不作为前后改善的比较对象。长时间增长是否稳定仍待使用同一程序、同一设置继续观察。

本轮没有用强制 GC、工作集修剪、降低到 30 FPS 或关闭阴影来制造低资源读数。曾测试文字 BitmapCache、缩短透明窗口高度等方向，收益不足或有已有窗口定位风险，没有采用。

## 正确性与视觉边界

- 增量读取检查覆盖半行 JSON / UTF-8、追加、截断、文件轮转、尾部原地重写、文件替换保留大小/时间戳，以及超出尾读范围后的重建。文件身份与尾部锚点用于识别代次；它不是任意位置文件改写的完整内容哈希。
- 索引最多保留 256 个近期文件，变更提示队列上限 1,024。来源的候选数量仍保持原来的 Codex 32 / WorkBuddy 24；日志初次尾读保持原有有界策略，不加载全部历史。监视器不是唯一真相，遗漏的新文件最多等待一分钟目录复核。
- 并行任务中单项完成、其它任务仍忙碌；工具调用等待及续接；未运行状态隐藏；配置保持；更新检查均有回归检查。
- 阴影缓存检查覆盖缩放抓取、大小变化、向上展开、快速反向开关、未变化几何复用。自动控件检查和 RenderTargetBitmap 截图不能替代真实桌面验收。
- **透明背景合成有轻微变化**：原阴影会使半透明岛内部偏暗，新方案裁掉内部阴影后背景略亮。布局和文字位置保持一致，但不能声称像素完全相同；桌面不同背景下的观感须由用户验收。

## 验证结果与复现

Release `win-x64` 构建：0 警告、0 错误。检查结果：PerformanceChecks 41、ActivityChecks 634、SettingsChecks 53、SettingsCardChecks 62、QuotaChecks 7、UpdaterChecks 12，共 809 项 PASS；另有 Node.js DeepSeek 状态语义检查通过。

测试宿主覆盖 App 启动入口，并设置预览模式，避免单实例 IPC、托盘、启动注册及配置保存。早期宿主受正常 App 单实例启动逻辑干扰，窗口被关闭的渲染读数已整体移到 `artifacts/performance/20261003/invalid-single-instance-host`，不用于上述结论。最终比较来自确认窗口仍可见的隔离宿主。

```powershell
dotnet run --project tests/PerformanceChecks -c Release
dotnet run --project tests/PerformanceChecks -c Release -- --benchmark native-marquee artifacts/performance/marquee.json
dotnet run --project tests/PerformanceChecks -c Release -- --monitor artifacts/performance/monitor.json
dotnet run --project tests/PerformanceChecks -c Release -- --quota artifacts/performance/quota.json
dotnet build YoyoClawCompanion/YoyoClawCompanion.csproj -c Release -r win-x64
node tests/harness-status-checks.mjs
```

本机原始结果放在 Git 忽略目录 `artifacts/performance/20261003`：滚动比较为 `before/after-marquee-repeat1/2.json`；检测为 `before-monitor-final.json`、`after-monitor-revision.json`；额度为 `before-quota-final.json`、`after-quota-version-metadata.json`；回归及构建日志在同一目录。

### 测试宿主隔离与配置恢复

收尾核对发现，控件测试宿主直接显示 MainWindow 时仍触发其 `OnLoaded`，写入测试默认设置。已移除该宿主的产品加载流程，并新增字节级检查，确认测试运行不会改写 `settings.json` 与 `presets.json`。

测试期间曾恢复本机配置。原始文件没有独立字节备份，因此不能声称恢复后与原文件逐字节一致；这项限制仍需人工验收。具体环境路径、配置字段和文件校验值不纳入公开文档，恢复记录保留在本机忽略目录中。

## 下一阶段

必须先处理验收发现的状态、交互或阴影观感回归。推荐按 [设置可用性方案](settings-usability-proposal.md) 先做可交互原型，再逐步实现单分类展示和按需创建；不同时重写全部外观。随后比较首次打开设置、当前分类常驻内存及长期运行增长。若私有内存仍持续增长，再开展有明确场景的原生分配跟踪。

可选项是进一步缩短非活跃来源的检查周期、完全隐藏后的后台节流及渲染专项跟踪；需要先确定通知延迟和恢复条件，不能以节省 CPU 为理由漏掉并行完成或待确认提醒。联动板块仍只规划，不在本轮实现。
