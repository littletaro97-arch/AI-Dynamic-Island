# GitHub 元数据待验收方案

本轮先完成分析和本地文档整改，等待所有者验收；**公开 Description / Topics 尚未修改**。没有尝试写入而被拒绝，不是权限失败。CLI 只读访问已验证可用；写权限未测试。

## 当前值（GitHub API 实测）

- Description：`Windows AI 状态灵动岛：Codex、WorkBuddy、YOYO Claw 任务与额度；安装版、便携版及应用内更新`
- Topics：空数组。
- 最新正式 Release：v0.8.0。
- GitHub 可识别许可证：null。

## 建议应用值

Description（明确额度是部分来源能力）：

> Windows AI agent status monitor & desktop widget (Dynamic Island) for Codex, WorkBuddy, YOYO Claw and DeepSeek Harness. Quota monitoring for supported agents, task tracking and notifications.

Topics：

`dynamic-island`, `windows`, `windows-11`, `desktop-widget`, `ai-agent`, `agent-monitor`, `codex`, `workbuddy`, `yoyo-claw`, `deepseek-harness`, `quota-monitor`, `usage-monitor`, `task-status`, `wpf`, `dotnet`

对应源码证据：WPF / .NET 8 csproj；CodexStatusService / CodexResetReminderTracker；WorkBuddyStatusService / WorkBuddyCreditsService；YoyoStatusService / YoyoQuotaService；DeepSeekStatusService。DeepSeek 只有任务状态，不添加通用 API、所有服务额度、实时性能等未经验证的宣称。

## 验收后应用（本轮未执行）

下列命令只修改这两类元数据，不 push 代码、不创建 Release，不修改更新清单。执行前重新读取当前值；若其他对话改过元数据，先核对差异，避免覆盖。

```powershell
gh api repos/littletaro97-arch/AI-Dynamic-Island --jq '{description,topics}'
gh repo edit littletaro97-arch/AI-Dynamic-Island --description 'Windows AI agent status monitor & desktop widget (Dynamic Island) for Codex, WorkBuddy, YOYO Claw and DeepSeek Harness. Quota monitoring for supported agents, task tracking and notifications.'
$topicList = @('dynamic-island','windows','windows-11','desktop-widget','ai-agent','agent-monitor','codex','workbuddy','yoyo-claw','deepseek-harness','quota-monitor','usage-monitor','task-status','wpf','dotnet')
# PUT 以完整列表替换 topics；重新核对后再执行。
@{names=$topicList} | ConvertTo-Json -Compress | gh api --method PUT repos/littletaro97-arch/AI-Dynamic-Island/topics --input -
gh api repos/littletaro97-arch/AI-Dynamic-Island --jq '{description,topics}'
```

人工操作：仓库 Code 页右侧 About → 齿轮 → 填写 Description 与 Topics → Save changes；逐项复核服务名称与功能范围。

回滚：将 Description 恢复为上述当前值；将 Topics 完整替换为空数组（仅适用于期间无他人新增的情况）。发布执行时记录操作者、Asia/Shanghai 时间、修改前后 API 值和错误信息。不得把元数据变更当作 README 已上线。
