# 通知唤起、设置入口与多助手额度滚动（验收迭代）

基线：v0.7.4 / eba6448。本轮仅本地提交与源码验收，不更新版本号、安装包、公开清单或 GitHub Release。

## 微信唤起

用户实际观测：QQ 与截图通知可以打开；微信提示“来源应用已运行，但无法唤起窗口”，来源名称为“微信”。旧代码有两条会影响微信的路径：可见辅助窗口会绕过快捷键，通知身份别名未列入白名单时也不会发送 Ctrl+Alt+W。本轮不把这些代码风险当作已经确认的实际通知根因。

- Windows AppsFolder 必须准确确认通知身份及其注册目标；运行进程还须匹配目标完整 EXE 路径。验证后，Weixin.exe / WeChat.exe 的注册通知别名也支持 Ctrl+Alt+W。
- 微信运行时优先使用自身快捷键，不再因辅助窗口可见而改用窗口激活。QQ 可见及最小化窗口、其它通知来源沿用原路径。
- 不强制显示隐藏 HWND，不以再次启动应用补救。物理修饰键按住时不注入；一次发送、最多等待 1.2 秒，失败保留原通知和托盘提示。
- 点击诊断仅在临时 STA 工作线程写入 `%LOCALAPPDATA%\YoyoClawCompanion\notification-open.log`，超过 64 KiB 时清空旧记录（单条追加会略超阈值）。只记身份、注册路径、窗口类别、快捷键发送/前台验证结果，不记录通知正文；没有常驻日志轮询。
- `NotificationProbe --identities` 可只读检查当前通知的来源身份，不输出消息正文。

## 设置与显示

- 修复“同步系统通知”和“Codex 待回答”的图标类型未转换问题；Codex 待回答复用 Codex 图标。
- 提醒板块增加带禁止图标的“关闭系统弹窗”按钮，打开 `ms-settings:notifications`，引导开启 Windows“请勿打扰”。按钮不会自动修改系统设置，也不显示虚假的开关状态；须保留通知总开关和通知中心。优先通知等例外仍可能有横幅。
- 全屏排查：当前保存配置为 Topmost=true、EnableFullscreenActiveOnly=true、DisplayMode=always。后两项组合仍会让全屏时的空闲岛隐藏；这是活动显示策略，和窗口层级置顶独立。本轮补充界面说明，不改用户配置，不保证普通 WPF 窗口覆盖独占全屏游戏。
- 两位以上助手执行时，溢出的额度摘要现在也会滚动，克隆文字包含所有执行中来源。沿用共享 WPF 动画时钟；相同状态不重启，隐藏/展开时停止。未添加逐帧 Dispatcher 回调或常驻定时器。

## 验证与人工验收

Release 构建无警告/错误。SettingsChecks 58、DisplayNotificationChecks 158（含本机微信快捷键检查 3）、ActivityChecks 640、SettingsCardChecks 65、PerformanceChecks 49，共 970 项通过。额度检查覆盖第二来源积分、状态重复刷新、隐藏与展开时停止；图标检查覆盖可绘制 ImageSource 及 Codex 复用。性能检查为控件/缓存机制检查，没有据此声称 CPU 百分比降低。

本机微信从隐藏托盘状态经生产 OpenAsync 唤起：SendInput 接受 6/6 输入，返回 Opened，未新增进程。此结果只证明该注册身份的原生快捷键链路，不能代替真实微信通知点击、窗口交互和关闭验收。待验收：手机发微信通知后点击岛、QQ/截图回归、两个执行中助手的额度滚动、系统设置入口及全屏时隐藏策略。

参考：[Windows 通知设置 URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)、[Windows 通知与请勿打扰](https://support.microsoft.com/en-us/windows/experience/notifications-and-do-not-disturb-in-windows)。