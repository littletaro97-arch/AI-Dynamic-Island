using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ToolTip = System.Windows.Controls.ToolTip;
using Brush = System.Windows.Media.Brush;

namespace YoyoClawCompanion.Controls;

// Own the hint lifetime per card rather than inheriting a tooltip from the whole panel.
internal sealed class SettingCardHint
{
    private readonly FrameworkElement _owner;
    private readonly ToolTip _hint;
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private int _transition;
    private bool _enabled = true;

    internal SettingCardHint(FrameworkElement owner)
    {
        _owner = owner;
        _hint = new ToolTip
        {
            Content = "长按开关卡片可调整当前板块的组件位置",
            PlacementTarget = owner, Placement = PlacementMode.Bottom,
            StaysOpen = true, IsHitTestVisible = false, Opacity = 0
        };
        _delay.Tick += (_,_) =>
        {
            _delay.Stop();
            if (!_enabled || !_owner.IsMouseOver || !_owner.IsLoaded) return;
            ++_transition;
            _hint.Background = _owner.TryFindResource("SettingsPopupBackground") as Brush;
            _hint.Foreground = _owner.TryFindResource("SettingsHintForeground") as Brush;
            _hint.BorderBrush = _owner.TryFindResource("SettingsInputBorder") as Brush;
            var previous = _hint.IsOpen ? _hint.Opacity : 0;
            _hint.IsOpen = true;
            _hint.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(previous,1,TimeSpan.FromMilliseconds(120)));
        };
        owner.MouseEnter += (_,_) => { if (_enabled) _delay.Start(); };
        owner.MouseLeave += (_,_) => Dismiss();
        owner.PreviewMouseDown += (_,_) => Dismiss();
        owner.Unloaded += (_,_) => { _delay.Stop(); ++_transition; _hint.IsOpen = false; };
    }

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) Dismiss();
    }

    private void Dismiss()
    {
        _delay.Stop();
        var version = ++_transition;
        if (!_hint.IsOpen) return;
        var fade = new DoubleAnimation(_hint.Opacity,0,TimeSpan.FromMilliseconds(140));
        fade.Completed += (_,_) => { if (version == _transition) _hint.IsOpen = false; };
        _hint.BeginAnimation(UIElement.OpacityProperty,fade);
    }
}
