# v0.7.1 发布与本地升级验收入口

2026-10-02，用户已授权发布。
发布源码提交：6216d30；Release tag v0.7.1 指向该提交。

- 安装 EXE 与便携 ZIP 生成成功；GitHub digest 与本地 SHA-256 一致。
- ZIP 内 status.mjs、Magicore SDK package.json 和第三方许可文件检查通过。
- 发布目录桥接只读实测 ok=true；安装编译日志确认纳入桥接和许可文件。
- 线上更新服务以 0.7.0 为当前版本时发现 0.7.1；安装版资产选择检查通过，匿名安装包下载入口返回 200。
- main 和发布分支已同步；公开更新清单已更新。
- 旧 v0.7.0 两个资产和 Release 元数据备份到 artifacts/backup-v0.7.0 后移除旧 Release；GitHub 当前只展示 v0.7.1 两个发行资产。
- 临时版正常退出，当前启动 E:\D-diskExpansionCabin\AI Dynamic Island\Installed\YoyoClawCompanion.exe，文件版本 0.7.0.0；已发送打开设置请求。

用户从原安装版设置中的“更新”执行检查、下载与安装。实际整机升级结果尚待用户验收；本轮未代替用户升级、未覆盖其已安装程序。
