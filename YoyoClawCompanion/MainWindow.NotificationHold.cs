using System.Diagnostics;

namespace YoyoClawCompanion;

public partial class MainWindow
{
    private readonly Stopwatch _notificationHoldClock = new();
    private TimeSpan _notificationHoldRemaining;
    private bool _notificationPointerInside;

    private bool IsReadingExpandedNotice => _expanded && (_notificationPointerInside || Island.IsMouseOver);

    private void BeginNotificationCountdown(TimeSpan duration)
    {
        CancelNotificationHold();
        _notificationHoldRemaining = duration;
        _notificationHoldActive = true;
        ResumeNotificationHoldAfterReading();
    }

    private void CancelNotificationHold()
    {
        _completionTimer.Stop();
        _notificationHoldClock.Reset();
        _notificationHoldRemaining = TimeSpan.Zero;
        _notificationHoldActive = false;
    }

    private void PauseNotificationHoldForReading()
    {
        if (!_notificationHoldActive || !IsReadingExpandedNotice || !_notificationHoldClock.IsRunning) return;
        _notificationHoldRemaining = MaxNotificationRemaining(_notificationHoldRemaining - _notificationHoldClock.Elapsed);
        _completionTimer.Stop();
        _notificationHoldClock.Reset();
    }

    private void ResumeNotificationHoldAfterReading()
    {
        if (!_notificationHoldActive || IsReadingExpandedNotice || _notificationHoldClock.IsRunning) return;
        _completionTimer.Interval = MaxNotificationRemaining(_notificationHoldRemaining);
        _notificationHoldClock.Restart();
        _completionTimer.Start();
    }

    private static TimeSpan MaxNotificationRemaining(TimeSpan value)
        => value > TimeSpan.FromMilliseconds(1) ? value : TimeSpan.FromMilliseconds(1);

    private void NotificationHold_Tick()
    {
        if (!_notificationHoldActive) { _completionTimer.Stop(); return; }
        if (IsReadingExpandedNotice) { PauseNotificationHoldForReading(); return; }
        // A callback queued before pause/restart must not end a new or resumed batch early.
        var remaining = _notificationHoldRemaining - _notificationHoldClock.Elapsed;
        if (remaining > TimeSpan.Zero) { _completionTimer.Interval = MaxNotificationRemaining(remaining); return; }
        EndCompletionNotice();
    }
}
