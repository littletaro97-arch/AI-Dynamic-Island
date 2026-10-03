using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class MainWindow
{
    private CodexStatus? _codexDecisionStatus;
    private string? _codexDecisionNoticeId;
    private bool HasCodexDecision => _codexDecisionStatus is { RequiresConfirmation: true }
        && IsProviderVisible("codex") && _settings.EnableCodexConfirmationNotifications;

    private void ShowCodexDecision()
    {
        var codex = _codexDecisionStatus!;
        _activeConfirmationNotice = $"Codex 需要你的回答 · {codex.ConfirmationPrompt ?? "请打开 Codex 查看并选择"}";
        if (_codexDecisionNoticeId == codex.ConfirmationId) return;
        _codexDecisionNoticeId = codex.ConfirmationId;
        PauseSystemToastForDecision();
        _activeCompletionNotice = _activeSystemNotice = null;
        HighlightProvider("Codex");
        RecentResultText.Text = ActiveNoticeText;
        BeginNotificationHold(); RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden); ExpandIsland(true);
    }
}
