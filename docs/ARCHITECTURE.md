# 架构与维护

主程序是 .NET 8 WPF，App 维护单实例与托盘；MainWindow 协调状态来源并呈现；SettingsWindow 修改保存的 AppSettings。Services 按来源隔离接口故障，公共窗口与路径服务支持多显示器、程序定位和启动注册。

数据流：本地来源 → 对应 Service → 状态快照 → MainWindow → 折叠/展开显示与完成提醒。Magicore 通过 Node.js 子进程桥接。WorkBuddy 辅助信号用于触发刷新，实际内容仍读取本地来源。

发布流：csproj 版本号 → dotnet self-contained publish → 便携 ZIP + Inno Setup EXE → GitHub Release。两种发行共享数据目录；新安装默认目录名为 AI Dynamic Island，不追加 Installed。已有安装升级保留原路径；安装版应与便携版使用不同目录。

Magicore 脚本、SDK 与第三方许可必须标记 ExcludeFromSingleFile=true 并独立落盘，Node.js 无法直接读取 .NET 单文件内部资源。Publish 完成后会检查关键外部文件，缺失时拒绝交付。

Assets/App.ico 是统一图标源，包含 16–256 px 多分辨率帧；EXE、WPF 窗口、托盘和安装器共同使用。scripts/Generate-AppIcon.ps1 按原有托盘图形生成该资源。

更新流：GitHub latest → 比较版本 → 按发行类型选包 → 下载 → SHA-256 验证 → 安装向导或外部 helper 替换。安装类型通过同目录 unins000.exe 识别。发布必须同时包含符合命名的两个资产及 GitHub digest。

卸载仅删除安装器管理的文件。开机注册仅在其命令指向当前安装目录时清除；用户设置默认保留。

回滚：保存历史 Release 到 artifacts/backup-v0.6.0；Git 保留旧提交。旧版发布源基线为 38e149f，本次修改独立在 codex/release-v0.7.0。不要用 reset --hard 删除用户本地文件。

## 检测与渲染性能

MainWindow 每次刷新共享一个 ProviderPresence 进程快照，来源服务仍可独立调用并采用各自运行判断。RecentSessionIndex 保存有界最近文件列表，监视变更并定期复核；JsonLineCursor 为 Codex、WorkBuddy 保留完整行偏移和文件身份，状态解析保留跨次调用及完成事件。服务关闭时释放目录监视器。DeepSeek 目录读取在后台执行。

Codex 额度 RPC 子进程创建后立即附加到私有 OwnedProcessScope，清理采用输入 EOF、短暂等待及 Job Object 回收，避免系统进程树遍历。该作用域只用于应用自己创建的进程。

CachedIslandShadow 将岛的轮廓阴影缓存为独立层，与滚动文字分离；设置方案名称仅在可见视口内运行 WPF 动画时钟。控件测试使用隔离 App 启动入口，避免正常单实例启动影响其它正在运行的版本。

具体数据和验收边界见 [2026-10-03 性能记录](PERFORMANCE-2026-10-03.md)。设置界面的下一阶段方向见 [可用性方案](settings-usability-proposal.md)，尚未迁移现有布局。

系统通知由 SystemNotificationService 在后台转换 Windows Toast，SystemNotificationTracker 去除历史与未变化快照。SystemNotificationQueue 保存有界待显示队列，MainWindow 按回复行数每批呈现最多三条并复用显示定时器。每条保留独立应用身份，NotificationTitleRule 按来源身份与具体标题屏蔽。设计、现场证据与验收边界见 [2026-10-06 通知迭代](NOTIFICATION-BATCHES-2026-10-06.md)。

设置下拉框和恢复通知菜单共用弹出面板资源；ExpandableSettingCard 共用开关、按钮与说明的动画，SettingsSwitchPanel 用稳定的一列/两列槽位管理展开覆盖。生命周期检查与回归范围见 [2026-10-07 复用与逻辑检查](SHARED-SETTINGS-REVIEW-2026-10-07.md)。
