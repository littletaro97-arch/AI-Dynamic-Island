# v0.7.4 发布验证

日期：2026-10-04。

- Release 标签 v0.7.4 指向 16421ff；程序本体由 2460c75 构建，版本 0.7.4.0，InformationalVersion 为 0.7.4+2460c75。标签与构建提交之间仅增加测试夹具稳定性修复，生产源码相同。
- 公开更新清单提交 3939547，已同步到 main，两个资产上传并核对后更新清单。
- Release 已公开并设为 Latest，仅提供安装版和便携版两个文件。
- Inno Setup 安装包编译成功，便携包 951 项内容核对通过，包含程序、外部桥接与许可，未混入用户 settings/presets/auth 配置或运行日志。
- DisplayNotificationChecks 152 项发布回归通过；UpdaterChecks 12 项离线及 3 项实时检查通过。实时匿名检查发现 0.7.4，安装模式正确选择安装 EXE。
- 公开 raw 更新清单版本、两个资产 URL 和 digest 核对通过；GitHub 服务端 digest 与本地一致。
- 两个包使用匿名 HTTP 完整下载，再次计算 SHA-256，均与本地一致。

| 文件 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| AI-Dynamic-Island-v0.7.4-win-x64-portable.zip | 76782506 | ced02eb1478b5c54c2c9d053dc0ab77df76af3993fbd3020e5b8ff42f378f3d0 |
| AI-Dynamic-Island-v0.7.4-win-x64-setup.exe | 54121272 | 508457acb0b7953cd750006116afd1c072308af1ef749104fbfdd2d26f3c77c1 |

旧 v0.7.3 资产和元数据已保存在被 Git 忽略的 artifacts/backup-v0.7.3，资产哈希与原 GitHub digest 一致。随后删除旧 Release，保留 Git 标签和源码历史，满足仅保留当前安装版、便携版两个下载包的要求。

本次没有执行真实安装升级或覆盖用户安装目录；安装目录复用、配置保留和跨机器快捷键仍由用户实际更新验收。正在运行的本地 0.7.3 验收版保持原样，可在应用内检查并更新。
