# 许可证、目录与启动名称审查

范围：GitHub main 基线 `7b70d0fefeee37f06d5d12e73c71241b0b3cc532`，本轮仅文档审查，不授予许可、不更改已发布资产。

## 必须由所有者确认

| 组件 | 当前证据 | 尚需确认 |
| --- | --- | --- |
| 第一方 WPF、适配器、原生 C++、桥接及脚本 | 无顶层 LICENSE；GitHub API `license: null` | 明确作者 / 贡献来源、拟授予的使用与再分发权、覆盖范围与例外。不能直接套用 MIT / Apache / GPL。 |
| YOYOClawCheckin 派生签到 | `THIRD_PARTY_NOTICES/YOYOClawCheckin-LICENSE.txt` 明确非商业、本人账户、禁代签 / 批量 / 绕过限制，并要求保留原许可与提示 | 新的第一方许可证如何保留该部分限制；现有文件不能被统一宽松许可覆盖。 |
| Magicore `@magicore/*` vendor SDK | `Build-Release.ps1` 要求本机 vendor SDK；node_modules 被忽略，干净克隆无 SDK 及其许可证。安装包来源不等于再分发授权 | 取得确切版本、来源安装包与授权条款，确认是否允许随安装 / 便携包再分发及所需通知。未获得证据前保持未确认。 |
| npm 直接及传递依赖 | package.json：`@grpc/grpc-js 1.14.4`、`bonjour-service 1.4.1`、`yaml 2.9.0`；lockfile 标注 Apache-2.0 / MIT 及传递依赖 BSD-3-Clause、ISC 等 | lockfile 标签不是完整许可证审计；需逐个检查实际交付 payload 的版本、LICENSE、NOTICE、版权声明，尤其 vendor 可能增加 lockfile 未列出的依赖。 |
| .NET self-contained runtime / MSVC / Windows 平台组件 | csproj 无显式第三方 PackageReference；打包包含 .NET runtime，C++ 使用 `/MT`；平台 API 引用不代表全部二进制是第一方代码 | 核对实际发布包、SDK / runtime 授权及通知保留；不以“无 PackageReference”推断“无第三方依赖”。 |
| 图标、品牌与 Magic 视界 | 源码含 App.ico；MagicIconService 从本机 MagicVisuals.exe 获取图标，源码不等于拥有品牌或素材权利 | 确认自制图标来源及展示权；不暗示第三方授权或背书。Magic 适配器源码和原生探测器与 Magicore vendor SDK 是不同对象。 |

目前只找到随项目保留的 YOYOClawCheckin 许可，不能据此得出整个仓库或全部发行包已获得完整授权。本轮没有扫描或修改既有 Release 二进制，不将源代码审查当作发行包许可合规验收。

参考：GitHub [仓库许可说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。上游许可可供核对：[grpc-node](https://github.com/grpc/grpc-node/blob/master/LICENSE)、[yaml](https://github.com/eemeli/yaml/blob/main/LICENSE)、[bonjour-service](https://github.com/onlxltd/bonjour-service/blob/master/LICENSE)；这些分支页面不是已交付具体版本的逐包证明。

建议所有者先完成 vendor 授权与代码权属确认，再决定顶层 LICENSE 的范围，并补充第三方清单。是否拆分受限签到模块是可选设计决定，本轮不改运行或打包方式。

## 推荐目录整理（仅方案）

根目录有五份历史 HTML 原型、设计 QA、早期测试与审查报告。它们可能分散第一次访问者注意力，也容易被当成当前界面证据。

建议未来另开经确认的整理变更，将原型归入 `docs/prototypes/`，早期报告归入 `docs/history/`，新增索引说明“历史 / 原型 / 非当前实机截图”。移动前检索所有链接并修正引用，保留 Git 历史；本轮没有移动、重命名或删除这些文件。现在通过 README 把下载、能力、限制与开发入口放在前面。

## 可选 EXE 命名统一（本轮不改）

统一为项目名可减少下载后的辨识成本，但 `YoyoClawCompanion.exe` 已用于启动脚本、安装卸载与快捷方式、自动更新替换、开机注册及用户已有路径。配置目录、单实例标识和程序集名又有独立兼容边界，不能简单全局替换。

后续若改名，应先列出所有引用与已安装版本升级路径，验证旧快捷方式 / 开机启动迁移、旧 updater 启动新 EXE、便携替换、卸载与回滚，并保留原配置兼容。本轮保留名称，只在 README 明确它是实际启动文件。
