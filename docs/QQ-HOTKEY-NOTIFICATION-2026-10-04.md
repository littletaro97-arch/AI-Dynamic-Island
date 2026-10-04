# QQ 通知快捷键唤起（2026-10-04）

基线：`68ef463`；分支：`codex/qq-notification-hotkey`。

用户确认本机 QQ 的 Ctrl+Alt+X 可以从托盘正常打开。通用窗口唤起仍是默认路径；只对已经由 Windows 注册入口和完整 EXE 路径确认运行、且存在合格隐藏主窗口的 QQ 添加这项适配。已显示或最小化窗口仍使用原通用逻辑，微信及其它来源不发送 QQ 快捷键。

发送顺序：Ctrl 按下、Alt 按下、X 按下、X 松开、Alt 松开、Ctrl 松开。使用一次 SendInput 批次，发送前检查 Ctrl/Alt/Shift/Win/X 是否已有按下状态，有则不发送。输入被阻止时不重试；部分插入时只补充释放本次仍按下的注入按键。处理依据 Windows [SendInput 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)。

之后在临时 STA 后台线程最多等待 1.2 秒，检查前台窗口属于已验证的 QQ 进程、可见、启用且未被系统标记无响应。快捷键未生效时保留原通知并提示从托盘打开，不强制显示 HWND，不启动新的 QQ 实例。没有常驻键盘钩子、没有新增后台轮询或定时器。这里使用用户已确认的 Ctrl+Alt+X；QQ 快捷键若被用户改动，需要同步更新适配，不能视为所有安装环境都相同。

## 验证

- DisplayNotificationChecks（含本机 opt-in QQ 唤起）：150 项通过。
- 检查包含适配身份限制、完整按下/释放顺序、Win32 INPUT 结构大小、物理按键冲突避让、输入失败不重试、部分输入清理，以及已有反向选择/通知/屏幕设置回归。
- 本机生产 `OpenAsync("QQ")` 返回 Opened，前台验证通过，未新增 QQ 进程。
- Win32 前台/启用/无响应标志检查不能代替应用实际按钮交互。发送手机 QQ 消息、点击岛通知、再操作并关闭 QQ 窗口仍由用户验收。

常规命令：`dotnet run --project tests/DisplayNotificationChecks -c Release`。设置 `ISLAND_QQ_HOTKEY_SMOKE=1` 会实际唤起正在运行的 QQ，只供主动本机验收使用。

本次只保存本地 Git、编译源码验收版，不生成安装包、不修改公开版本号、不推送 GitHub。
