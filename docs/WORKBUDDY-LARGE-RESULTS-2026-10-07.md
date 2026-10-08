# WorkBuddy 已结束但仍显示执行中

## 证据与关联

用户确认没有执行任务。生产解析器对当前会话仍返回 Busy=true、两个 pendingCalls、Completed=false；原始记录按调用编号核对则所有工具都已返回，末尾也有带有效统计的最终答复。

根因是两条工具返回记录超过 JsonLineTailReader 的 256 KiB 单记录限制，旧读取器直接丢弃它们。WorkBuddy 完成判定又要求没有未返回工具，两者组合导致最终答复被挡，误报持续到原有的两小时 pending 保鲜期结束。

061070d 的生命周期整理没有改动 WorkBuddyStatusService 或该读取器，不能把这次 busy 直接归因于关闭/重置修改；但上一轮所谓全面检查缺少大工具返回覆盖，未发现这处规则冲突。这是检查遗漏，本次补上具体证据和回归。

## 修复边界

保持原 1 MiB 单次扫描、256 KiB 正常记录上限。对已完整换行、超过单记录上限的工具返回，用 Utf8JsonReader 在既有 byte buffer 中验证完整 JSON，仅提取根级 type、callId 并生成很小的生命周期信封；不构造庞大的工具输出字符串或完整 JsonDocument。无关大记录仍忽略，嵌套输出的同名字段不能冒充根级字段。

没有提高两小时超时，也不强制空闲、清空真正执行中的调用，最终答复仍须有效结束统计；中途文本和工具返回本身不会触发完成提醒。不是靠重启 WorkBuddy 或修改它的日志掩盖问题。

## 验证

原会话只读重放后 PendingCount=0、Completed=true、IsBusy=false；最近 24 个会话均无执行中状态。未修改、复制到 Git 或输出聊天正文。

WorkBuddyCompletionChecks 从 17 项增加至 25 项：覆盖 300 KiB 工具结果、标识位于正文之后、增量读取、未换行的大记录、畸形大记录、结果后仍有中途答复，以及真正最终答复恢复空闲；返回给解析器的字符串保持很小。ActivityChecks 640、DisplayNotificationChecks 259、PerformanceChecks 49 项通过，共 973 项。自动检查不代替真实桌面状态验收，也不证明 CPU 百分比下降。

本轮本地提交、编译并切换验收版，不推送 GitHub、不生成正式发布包。用户设置和 WorkBuddy 进程保持不变。
