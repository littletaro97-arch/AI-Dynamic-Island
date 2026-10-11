using System.Windows;
using System.Windows.Media.Animation;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class SettingsWindow
{
    private string _previewDisplayId = DisplayPlacement.PrimaryId;
    private int _displayPreviewVersion;

    private DisplayInfo? PreviewDisplay
    {
        get
        {
            var displays = _island.ConnectedDisplays;
            return displays.Count == 0 ? null : DisplayPlacement.Resolve(displays, _previewDisplayId);
        }
    }

    private void InitializeDisplayPreview()
    {
        _island.DisplaysChanged += Island_DisplaysChanged;
        Closed += (_, _) => { ++_displayPreviewVersion; _island.DisplaysChanged -= Island_DisplaysChanged; };
        RefreshDisplayPreview(false);
    }

    private void Island_DisplaysChanged(object? sender, EventArgs args) => RefreshDisplayPreview(true);

    private void SwitchDisplay_Click(object sender, RoutedEventArgs args)
    {
        var displays = _island.ConnectedDisplays;
        if (displays.Count < 2) return;
        var current = DisplayPlacement.Resolve(displays, _previewDisplayId);
        var index = displays.ToList().FindIndex(d => d.Id == current.Id);
        var target = displays[(index + 1) % displays.Count];
        _previewDisplayId = target.IsPrimary ? DisplayPlacement.PrimaryId : target.Id;
        _island.GoToDisplay(target);
        RefreshDisplayPreview(true);
    }

    private void SetSecondaryDisplayControl(FrameworkElement control, bool visible, bool animate, int version)
    {
        if (!animate)
        {
            control.BeginAnimation(OpacityProperty, null);
            control.Opacity = visible ? 1 : 0;
            control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        if (visible) control.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            if (version != _displayPreviewVersion || _closed) return;
            control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        };
        control.BeginAnimation(OpacityProperty, fade);
    }

    private void RefreshDisplayPreview(bool animate)
    {
        if (_closed) return;
        var displays = _island.ConnectedDisplays;
        if (displays.Count == 0) return;
        var display = DisplayPlacement.Resolve(displays, _previewDisplayId);
        if (_previewDisplayId != DisplayPlacement.PrimaryId && display.IsPrimary) _previewDisplayId = DisplayPlacement.PrimaryId;
        var hasSecondary = displays.Count > 1;
        SwitchDisplayButton.IsEnabled = hasSecondary;
        SwitchDisplayButton.Content = displays.Count > 2 ? "前往下一屏" : display.IsPrimary ? "前往副屏" : "返回主屏";
        DisplayConnectionText.Text = displays.Count > 1
            ? "主屏、副屏分别记忆位置；副屏断开时回到主屏，重新连接后恢复。"
            : "";
        var version = ++_displayPreviewVersion;
        SetSecondaryDisplayControl(SwitchDisplayButton, hasSecondary, animate, version);
        SetSecondaryDisplayControl(DisplayConnectionText, hasSecondary, animate, version);
        void Apply()
        {
            DisplayRoleText.Text = display.Label;
            PositionPreviewSurface.Width = display.IsPortrait ? 360 : 560;
            PositionPreviewSurface.Height = Math.Clamp(PositionPreviewSurface.Width * display.Bounds.Height / display.Bounds.Width, 250, 640);
            foreach (var button in new[] { TopLeftPreset, TopCenterPreset, TopRightPreset, BottomLeftPreset, BottomCenterPreset, BottomRightPreset })
            {
                button.FontSize = display.IsPortrait ? 20 : 13;
                button.Height = display.IsPortrait ? 50 : 38;
            }
            DisplayRoleText.FontSize = display.IsPortrait ? 20 : 13;
            PositionModeText.FontSize = display.IsPortrait ? 18 : 13;
            UpdatePositionControls(_island.PositionForDisplay(display).Preset);
        }
        if (!animate) { Apply(); PositionPreviewSurface.Opacity = 1; PositionPreviewViewbox.Height = display.IsPortrait ? 380 : 270; return; }
        var fade = new DoubleAnimation(PositionPreviewSurface.Opacity, 0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            if (version != _displayPreviewVersion) return;
            Apply();
            PositionPreviewSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        };
        PositionPreviewSurface.BeginAnimation(OpacityProperty, fade);
        PositionPreviewViewbox.BeginAnimation(HeightProperty, new DoubleAnimation(PositionPreviewViewbox.ActualHeight,
            display.IsPortrait ? 380 : 270, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase() });
    }
}
