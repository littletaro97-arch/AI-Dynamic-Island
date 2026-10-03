# WorkBuddy 中途答复误触发完成提醒

## 原因与本轮修改

WorkBuddy 的会话记录中，中途答复和最终答复都会使用 `type: message`、`role: assistant`、`status: completed`。这里的 completed 只表示这条消息结束输出，不能独立作为整轮任务完成的证据。原解析器把每条这种消息加入 Completions，而通知模块允许并行任务单项完成时展开，导致中途文本也触发了展开。

用户提供的会话中，中途答复“我收紧一点重做”后还有 Bash、Read 等调用；约一分钟后的最终答复才带有 `message.usage` 和 `providerData.usage / rawUsage` 的本轮统计。检视本机最近 30 个会话：719 条中途完成消息后接工具调用且无结束统计；99 条最终答复带统计，之后是新用户消息或文件结束。样本覆盖 Space-Bunny、GLM、DeepSeek、Kimi 五种模型标识。这是本机当前日志格式的验证，不能视为第三方承诺的永久协议。

本轮 WorkBuddy 解析器只有在消息输出完毕、有有效结束统计且无待返回工具/待确认问题时，才更新最终答复与完成事件。支持观察到的 `providerData.usage.outputTokens`、`providerData.rawUsage.completion_tokens`、`message.usage.output_tokens`；空、null 或非数值统计不作为完成证据。不依赖答复里有没有“完成”两个字，也不以等待几秒或强制转为空闲掩盖问题。

## 保留的通知规则

- 同一个任务中途输出：保持执行中，不触发完成展开。
- 单个任务真正完成且无其它运行任务：转为空闲，同时触发完成提醒。
- 一个任务真正完成、另一个会话仍在执行：继续显示执行中，展开提醒已完成的那个任务。
- 旧完成历史、重复轮询不会重复提醒；待确认提醒的优先级保持不变。

## 验证

真实会话分段回放：中途消息、随后工具执行均无完成事件；最终答复变为空闲且只有一条完成事件；继续回放完整会话不会重新加入那条中途文本。仅复制会话到测试输出目录，原 WorkBuddy 日志不修改、不上传。

新增 WorkBuddyCompletionChecks 17 项、真实回放 4 项通过；PerformanceChecks 41 项、ActivityChecks 634 项通过，共 696 项。Release win-x64 构建 0 警告、0 错误。回归前后的用户 settings.json、presets.json 校验值一致。

```powershell
dotnet run --project tests/WorkBuddyCompletionChecks -c Release
# 可选：指定自己的本机会话文件进行分段回放
dotnet run --project tests/WorkBuddyCompletionChecks -c Release -- --replay '完整会话文件路径.jsonl'
dotnet run --project tests/PerformanceChecks -c Release
dotnet run --project tests/ActivityChecks -c Release
```

`--replay` 是针对本次报告文本的诊断入口，要求会话包含该条中途文本；一般会话不应使用此入口。原始验证日志位于本机 Git 忽略目录 `artifacts/workbuddy-completion-20261003`。

版本号暂不提升，未生成安装包/便携包或推送 GitHub。验收重点是：真实 WorkBuddy 工具执行中多次输出文本不会自动展开；最终答复才提醒；并行任务仍保留单项完成提醒。如果 WorkBuddy 后续改变结束统计格式，需要重新验证并更新解析，而不能重新退回仅判断 message.status。
