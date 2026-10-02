# 验收版布局、滚动与卡片拖动修复

基线：`7f932ca`。分支：`codex/preview-layout-drag-smoothness`。
保持当前版本号 0.7.2；本轮只提交源码并启动临时验收版，不生成安装包、不推送或修改 GitHub Release。

## 修改与原因

1. 四个顶部状态点统一为 8 × 8 DIP，同一按钮高度和垂直居中模板，避免单个内容偏移。
2. 展开列表的名称列由固定 102 DIP 改为按内容分配宽度，使 DeepSeek Harness 可以完整显示。
3. 隐藏助手时，名称、状态、积分、状态点和行按钮一起隐藏，再压缩剩余行。原实现只隐藏行按钮，零高度行中的文字仍会绘制，和移入原位置的助手重叠。保留原有淡出、高度调整和淡入动画，以及重新运行后恢复检测的规则。
4. 移除顶部额度横向滚动的 30 Hz DispatcherTimer。使用共享的 WPF 线性循环动画时钟驱动两份文字，避免逐帧托管回调，并保持两份文字同步；展开、隐藏、非额度状态或无额度内容时移除动画时钟。滚动速度设置仍按 DIP/秒生效。
5. GitHub 地址右侧“跳转”和“复制地址”统一显式垂直边距。原先“跳转”继承按钮样式的顶部 8 DIP 边距，造成上下错位。
6. 卡片拖动由原生 DoDragDrop 改为面板捕获鼠标并平移卡片本身。保留抓取缩小效果，提高拖动卡片的绘制层级；松手后只在当前板块内排序，越界、失去捕获或退出编辑时动画返回原位置。缩放和平移分别应用，落位使用原视觉位置向新槽位过渡。

## 验证

- `dotnet build YoyoClawCompanion/YoyoClawCompanion.csproj`：通过，0 错误、0 警告。
- `ActivityChecks`：向上/向下展开的 16 种助手显示组合、整行隐藏、回复行隔离、四点水平对齐、名称实际宽度、DeepSeek 被隐藏后 YOYO 移入原行、同步动画时钟与展开停止检查通过；原并行任务完成检测检查通过。
- `SettingsChecks`：780/920 DIP 设置窗口宽度下更新区域按钮边界和底部两按钮上下边界一致，原侧栏/进度条过渡检查通过。
- `SettingsCardChecks`：鼠标位移对应卡片位移、反复移动无累积漂移、抓取缩放、取消归位、同板块落位和跨板块拒绝检查通过。
- 控件渲染图：`artifacts/layout-drag-evidence/four-provider-layout.png` 与 `dismissed-harness-layout.png` 已检查，四点同高、名称完整、隐藏后无重叠。这是控件渲染验证，不能替代真实鼠标与显示器的视觉验收。

## 滚动验收边界

此处修复的是灵动岛顶部额度文字横向滚动。没有固定 30/60 帧的应用层定时器；实际呈现由 WPF 和桌面合成决定，未宣称已经测得显示器刷新率对应的实际帧率，也未宣称整机 CPU 用量下降。请在当前显示器上验收滚动和拖动观感。

参考：[WPF MediaContext 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs)，其呈现调度使用桌面合成返回的显示刷新信息。本轮采用默认动画调度，不通过高频计时器或全局帧率配置覆盖它。

临时验收输出：`.build-check/layout-drag-preview/`。启动前在 `artifacts/preview-backup/layout-drag-时间戳/` 备份用户配置；仅退出旧灵动岛进程并启动新临时目录中的 `YoyoClawCompanion.exe --preview`，安装目录保留原版本。
回滚源码可在该分支使用 `git revert` 撤销本轮提交；程序可退出验收版后启动原安装目录中的 EXE。
