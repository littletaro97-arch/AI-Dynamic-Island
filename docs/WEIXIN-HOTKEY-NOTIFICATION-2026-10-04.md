# 微信通知快捷键唤起（2026-10-04）

基线 `21e212e`，分支 `codex/weixin-notification-hotkey`。

微信托盘唤起采用用户指定的 Ctrl+Alt+W，QQ 保持 Ctrl+Alt+X。两者共用身份校验、一次性按键注入、按键释放、物理按键冲突避让和最多 1.2 秒的前台确认。适配只用于已由注册入口完整路径验证的运行进程。已经显示或最小化的窗口仍走通用窗口激活逻辑；隐藏在托盘或暂时没有合格主窗口时，才尝试对应应用的快捷键。其它应用不发送这两个快捷键。

支持本机以 Weixin.exe 完整路径作为 AppUserModelID 的注册方式，以及名称匹配的 Weixin/WeChat/Tencent.WeChat 身份和 Weixin.exe/WeChat.exe 程序。身份与程序不匹配、没有验证到运行实例时不发送。发送失败或未到达前台则保留通知并提示从托盘打开，不强制显示隐藏 HWND，不再次启动进程。

DisplayNotificationChecks 155 项通过，含本机微信 opt-in 实际唤起：生产 OpenAsync 返回 Opened，前台句柄验证通过，未新增微信进程。检查也保留 QQ 快捷键序列与冲突避让、隐藏窗口保护、反向选择及显示器设置回归。用户仍需验收手机消息→岛通知→点击→微信按钮交互及关闭。

没有增加常驻线程、定时器、键盘钩子或后台轮询；临时检查只在用户点击时执行。本次未进行新的资源基准测试，不声称 CPU/内存占用下降。只保存本地 Git、编译并启动源码验收版；不生成安装包，不推送 GitHub。

普通检查：`dotnet run --project tests/DisplayNotificationChecks -c Release`。本机设置 `ISLAND_WEIXIN_HOTKEY_SMOKE=1` 可执行真实微信唤起检查，会改变前台窗口，仅供主动桌面验收。
