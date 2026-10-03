# 通知点击与滚动优化（本地验收）

基线为 `8059e2b`，分支 `codex/notification-open-scroll-performance`。应用仍标记 0.7.3；不生成正式发布包，不推送 GitHub，不修改更新清单或安装目录。验收构建位于 `.build-check/notification-scroll-preview`。

## 通知点击

- 通知文本区域支持点击和键盘激活，使用 Windows 返回的 AppUserModelId 打开来源应用。普通任务结果不会出现这层点击区域，Codex 等待回答时也不会误开被暂时打断的通知来源。
- 通过 Windows Shell 的 AppsFolder 注册入口打开应用，不把通知标题、正文、网址或文件路径当成执行参数。空标识、控制字符、路径或 URL 标识被拒绝；启动失败保留通知并显示反馈。
- 成功交给 Shell 后结束当前提示，继续处理待显示消息并恢复正常状态。保留 Windows 原通知。
- Shell 解析与激活在短时 STA 后台线程执行，点击后显示“正在打开”并防止重复提交；操作完成即释放线程，期间不阻塞 WPF。等待期间通知被替换或结束时，旧操作的完成结果不覆盖新提示。
- 此接口提供的是来源应用身份及通知视觉文本，当前只实现应用入口跳转，不重放原生通知的会话、按钮或业务参数。Shell 返回成功也不代表应用一定已显示正确业务页面；真实来源应用的前台激活仍需桌面验收。

依据：[UserNotification 属性](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.usernotification)、[ShellExecuteEx 返回值及 COM 约束](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw)。

## 滚动与界面线程

1. 设置页滚轮从直接跳转改成 180 毫秒缓动；连续输入累积目标，反向输入从当前位置响应。目录跳转保留 360 毫秒动画，完成后释放动画时钟，旧完成回调不覆盖新目标。
2. 仅在滚动期间缓存进入视口的设置卡片，排除有持续跑马灯的方案板块。当前缓存像素预算 200 万（RGBA 约 7.6 MiB，不含驱动、效果、边缘及内部缓冲开销）。高 DPI 下缓存采样倍率为 0.65；停止滚动约 240 毫秒后恢复原生绘制。最小化、隐藏、关闭后停止缓存释放定时器并移除缓存。
3. 收起状态的跑马灯合并重复刷新；文字、宽度和速度不变时保留原时钟。视口宽度改变但文字仍溢出时，也保持滚动相位。时钟数字使用等宽数字排版，减少秒数变化带来的布局晃动。展开、隐藏时停止跑马灯。
4. YOYO 积分文件及日志解析、WorkBuddy 积分 IPC 准备、批量系统通知属性转换移到后台。通知授权仍从界面线程调用，转换结果回到界面线程后检查启用状态和代次，避免关闭功能后旧读取结果重新显示。
5. 最近结果文本相同则不重复赋值。性能优先要求已写入 `AGENTS.md`，供后续迭代遵循。

没有降低整个应用或显示器分辨率，没有限制到 30/60 FPS，没有强制 GC 或工作集修剪。依据：[WPF 像素与渲染开销](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware)、[BitmapCache.RenderAtScale](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.bitmapcache.renderatscale)、[通知监听器线程模型与授权要求](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.management.usernotificationlistener)。

## 对比证据与限制

同一 Release 测试宿主、200% 缩放、Render Tier 2、16 个逻辑处理器；预热 3.5 秒后采样约 15 秒。设置滚动负载用 16 毫秒定时输入往返滚动整页，两侧输入相同；它用于比较负载，不等于鼠标滚轮交互的全部过程。CPU 是进程 CPU 时间除以墙钟和逻辑处理器数。

| 同条件成对测量 | 基线 CPU | 修改后 CPU | 基线私有提交 | 修改后私有提交 |
| --- | ---: | ---: | ---: | ---: |
| 第一组设置滚动 | 0.999% | 0.627% | 334.8 MiB | 340.5 MiB |
| 再次独立成对测量 | 1.738% | 1.050% | 457.6 MiB | 483.9 MiB |

两组 CPU 分别下降约 37% / 40%，私有提交增加约 6 / 26 MiB。环境波动明显，不能把缓存像素预算当作进程内存增加的硬上限，不能声称内存已下降。两组设置场景的渲染回调间隔 P95 都约 18.18 毫秒；长于 25 毫秒的间隔未稳定下降，因此本轮不声称显示器实际帧率已提高。新的滚轮缓动和运动时较低采样的观感由用户验收。

独立长文字跑马灯 CPU 为 1.154% → 1.204%，未测得 CPU 改善；渲染回调 P95 均约 6.06 毫秒。该负载关闭检测和通知，无法证明真实文件读取、通知转换引发的界面阻塞已完全消失。改进主要是相位连续和减少界面线程上的阻塞路径。

完整清晰度卡片缓存及单独文字缓存的代价或收益不合适，未采用。曾有一组复测与其它控件宿主同时运行，内存增幅明显，记录保留但不计入上表；另一次独立成对测量已补做。临时自定义滚动容器没有表现出独立收益，已移除。

本机原始 JSON、检查日志保存在 Git 忽略目录 `artifacts/performance/notification-scroll`。上表采用 `before-settings.json` / `after-settings.json` 和 `isolated-before-settings.json` / `isolated-after-settings.json`。仅测试宿主订阅 Rendering 回调用于采样，产品没有增加常驻逐帧托管回调。

## 验证与验收

Release 构建 0 警告、0 错误。检查：PerformanceChecks 49、DisplayNotificationChecks 111（包含真实通知读取及 STA Shell 解析的 3 项检查）、SettingsChecks 53、SettingsCardChecks 65、ActivityChecks 634、WorkBuddyCompletionChecks 17、QuotaChecks 7，共 936 项 PASS。

真实 Windows 监听器无需重新请求权限即可读取当前通知基线，未重放历史消息。无效应用身份的拒绝、启动失败反馈、待回答优先级、普通结果无点击覆盖层、滚动中间值、快速反向、闲置/最小化释放缓存、跑马灯持续相位、用户配置未被控件检查改写均通过自动检查。

用户重点验收：

1. 等待一条新系统通知，点击内容是否打开正确来源；不能打开时是否保留文本并提示。
2. 长内容通知与 Codex 待回答同时出现时，是否仍优先展示问题。
3. 设置页连续滚轮、突然反向、目录跳转，以及拖动开关时布局是否正常；停下后的文字是否恢复清晰。
4. 所有助手就绪时观察收起状态的额度滚动，秒数变化或检测刷新时是否还跳顿。
5. 长时间使用后的 CPU / 私有提交是否稳定；当前短时测量不证明没有长期增长。

本轮代码可用 `git revert <本轮提交号>` 回退。用户配置和正式安装目录不作为源码回滚目标，启动验收版前另行保存配置副本。
