# v0.7.0 基线上的本地细节修正（未发布）

日期：2026-10-01。源码基线 17030b2；已发布包对应 a7e5f94。
本地分支：codex/refine-v0.7.0-install-icons-yoyo。
保持版本号 0.7.0；用户明确要求后再确定下一版版本号、生成发布包及推送。

## 安装目录

新安装默认末级目录改为 AI Dynamic Island，移除 Installed。既有安装升级沿用原目录，以免留下旧程序、快捷方式和启动项；本轮没有移动 E:\D-diskExpansionCabin\AI Dynamic Island\Installed。

## 图标

原 EXE 未配置 ApplicationIcon，快捷方式与任务栏可能展示通用程序图标。现在从原托盘的深色圆角背景与绿/紫/黄三点图形生成多分辨率 ICO，统一 EXE、窗口、托盘和安装器。

## YOYO 排查

当前安装版文件版本为 0.7.0.0，实际安装目录仅有两个 EXE 和卸载 DAT，Magicore 目录没有脚本或 SDK 文件。原因是发布改为 .NET 单文件后没有将外部内容标记 ExcludeFromSingleFile。

积分读取独立的 quota.json，所以任务接口失效时积分仍可显示。源码目录与本地编译目录中的 status.mjs 只读调用当前 YOYO 均返回 ok=true，证明此次不是当前 YOYO 接口本身失效。

修正：将桥接与第三方许可内容明确作为外部文件发布，并在 Publish 完成后加入关键文件检查。

## 验证

- 项目 Release 本地编译：0 警告、0 错误。
- 单文件发布项目元数据：status.mjs、SDK package.json、第三方许可均为 ExcludeFromSingleFile=true。
- 编译目录桥接只读实测返回 ok=true；未输出会话内容或读取凭据文件进行诊断。
- 从编译后的 EXE 提取图标，中心色与统一图标源一致。
- 本轮未执行 dotnet publish、未编译安装包、未改写已安装应用、未重启任何应用、未推送或修改 GitHub Release。
- Windows 任务栏与快捷方式的真实显示、实际新安装路径和最终发布包桥接落盘，留待用户授权构建后验收。

回滚使用 git revert 本分支的修正提交；17030b2 是本轮开始前基线。保留用户已有未跟踪原型文件。
