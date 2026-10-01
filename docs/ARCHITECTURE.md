# 架构与维护

主程序是 .NET 8 WPF，App 维护单实例与托盘；MainWindow 协调状态来源并呈现；SettingsWindow 修改保存的 AppSettings。Services 按来源隔离接口故障，公共窗口与路径服务支持多显示器、程序定位和启动注册。

数据流：本地来源 → 对应 Service → 状态快照 → MainWindow → 折叠/展开显示与完成提醒。Magicore 通过 Node.js 子进程桥接。WorkBuddy 辅助信号用于触发刷新，实际内容仍读取本地来源。

发布流：csproj 版本号 → dotnet self-contained publish → 便携 ZIP + Inno Setup EXE → GitHub Release。两种发行共享数据目录，但安装目录单独使用 Installed 子目录，避免覆盖已有便携版。

更新流：GitHub latest → 比较版本 → 按发行类型选包 → 下载 → SHA-256 验证 → 安装向导或外部 helper 替换。安装类型通过同目录 unins000.exe 识别。发布必须同时包含符合命名的两个资产及 GitHub digest。

卸载仅删除安装器管理的文件。开机注册仅在其命令指向当前安装目录时清除；用户设置默认保留。

回滚：保存历史 Release 到 artifacts/backup-v0.6.0；Git 保留旧提交。旧版发布源基线为 38e149f，本次修改独立在 codex/release-v0.7.0。不要用 reset --hard 删除用户本地文件。
