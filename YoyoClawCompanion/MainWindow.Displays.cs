using Microsoft.Win32;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using YoyoClawCompanion.Services;
using Point = System.Windows.Point;
using NativeWindow = YoyoClawCompanion.Services.NativeWindow;

namespace YoyoClawCompanion;

public partial class MainWindow
{
    private bool _displayPlacementsInitialized;
    private int _displayMoveVersion;
    private readonly DispatcherTimer _displayChangeTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    internal event EventHandler? DisplaysChanged;

    internal IReadOnlyList<DisplayInfo> ConnectedDisplays => NativeWindow.GetDisplays();
    internal DisplayPosition PositionForDisplay(DisplayInfo display)
    {
        if (display.IsPrimary)
        {
            if (_settings.PrimaryDisplayPosition is { } saved) return saved;
            if (_displayPlacementsInitialized) return _settings.PrimaryDisplayPosition = new DisplayPosition();
            return new DisplayPosition { Preset = NormalizePositionPreset(_settings.PositionPreset) };
        }
        _settings.SecondaryDisplayPositions ??= new();
        if (!_settings.SecondaryDisplayPositions.TryGetValue(display.Id, out var position))
            _settings.SecondaryDisplayPositions[display.Id] = position = new DisplayPosition();
        return position;
    }

    private void InitializeDisplayPlacements()
    {
        var displays = ConnectedDisplays;
        if (displays.Count == 0) return;
        // Upgrade from the old global coordinates without moving the user's custom position.
        if (_settings.PrimaryDisplayPosition is null)
        {
            var current = DisplayAtIsland(displays);
            _settings.PrimaryDisplayPosition = new DisplayPosition();
            _settings.PreferredDisplayId = current.IsPrimary ? DisplayPlacement.PrimaryId : current.Id;
            CaptureDisplayPosition(current);
        }
        _displayPlacementsInitialized = true;
        RestorePreferredDisplay(false);
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        _displayChangeTimer.Tick += (_, _) =>
        {
            _displayChangeTimer.Stop();
            RestorePreferredDisplay(true);
            DisplaysChanged?.Invoke(this, EventArgs.Empty);
        };
        Closed += (_, _) => { ++_displayMoveVersion; _displayChangeTimer.Stop(); SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged; };
    }

    private void OnDisplaysChanged(object? sender, EventArgs args)
        => Dispatcher.BeginInvoke(() => { _displayChangeTimer.Stop(); _displayChangeTimer.Start(); });

    private DisplayInfo DisplayAtIsland(IReadOnlyList<DisplayInfo> displays)
    {
        var bounds = GetIslandScreenPixelBounds();
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        return displays.FirstOrDefault(d => d.Bounds.Contains(center))
            ?? displays.OrderBy(d => (new Point(d.Bounds.Left + d.Bounds.Width / 2, d.Bounds.Top + d.Bounds.Height / 2) - center).LengthSquared).First();
    }

    private void CaptureDisplayPosition(DisplayInfo? display = null)
    {
        if (_expanded || _islandAnimationInProgress) return;
        var displays = ConnectedDisplays;
        if (displays.Count == 0) return;
        display ??= DisplayAtIsland(displays);
        var bounds = GetIslandScreenPixelBounds();
        var saved = PositionForDisplay(display);
        saved.Preset = NormalizePositionPreset(_settings.PositionPreset);
        saved.OffsetX = bounds.Left - display.Bounds.Left;
        saved.OffsetY = bounds.Top - display.Bounds.Top;
    }

    internal void GoToDisplay(DisplayInfo display)
    {
        if (_displayPlacementsInitialized) SavePosition();
        _settings.PreferredDisplayId = display.IsPrimary ? DisplayPlacement.PrimaryId : display.Id;
        TransitionDisplayPlacement(display, PositionForDisplay(display), true);
    }

    internal void MoveToDisplayPreset(string preset, DisplayInfo display)
    {
        if (_displayPlacementsInitialized) SavePosition();
        var position = PositionForDisplay(display);
        position.Preset = NormalizePositionPreset(preset);
        _settings.PreferredDisplayId = display.IsPrimary ? DisplayPlacement.PrimaryId : display.Id;
        TransitionDisplayPlacement(display, position, true);
    }

    private void RestorePreferredDisplay(bool animate)
    {
        var displays = ConnectedDisplays;
        if (displays.Count == 0) return;
        // Keep the preferred secondary identity and its preset when disconnected.
        var display = DisplayPlacement.Resolve(displays, _settings.PreferredDisplayId);
        TransitionDisplayPlacement(display, PositionForDisplay(display), animate);
    }

    internal void NudgeDisplayPosition(DisplayInfo display, double horizontal, double vertical)
    {
        if (_displayPlacementsInitialized) SavePosition();
        var position = PositionForDisplay(display);
        var target = DisplayPlacement.Target(display.Bounds, GetIslandScreenPixelBounds().Size, position);
        var delta = DipsToDevicePixels(new Vector(horizontal, vertical));
        position.Preset = "custom";
        position.OffsetX = target.X - display.Bounds.Left + delta.X;
        position.OffsetY = target.Y - display.Bounds.Top + delta.Y;
        _settings.PreferredDisplayId = display.IsPrimary ? DisplayPlacement.PrimaryId : display.Id;
        TransitionDisplayPlacement(display, position, true);
    }

    private void TransitionDisplayPlacement(DisplayInfo display, DisplayPosition position, bool animate)
    {
        var version = ++_displayMoveVersion;
        if (!animate || !IsLoaded) { PlaceOnDisplay(display, position); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            if (version != _displayMoveVersion) return;
            var displays = ConnectedDisplays;
            if (displays.Count == 0) return;
            var current = DisplayPlacement.Resolve(displays, display.IsPrimary ? DisplayPlacement.PrimaryId : display.Id);
            PlaceOnDisplay(current, current.Id == display.Id ? position : PositionForDisplay(current));
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() });
        };
        BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private void PlaceOnDisplay(DisplayInfo display, DisplayPosition position)
    {
        if (_expanded || _islandAnimationInProgress) CompleteCollapseImmediately();
        Island.UpdateLayout();
        var bounds = GetIslandScreenPixelBounds();
        var target = DisplayPlacement.Target(display.Bounds, bounds.Size, position);
        var correction = DevicePixelsToDips(target - bounds.TopLeft);
        Left += correction.X; Top += correction.Y;
        _settings.PositionPreset = NormalizePositionPreset(position.Preset);
        UpdateCollapsedAnchorFromCurrentGeometry();
        OrientCollapsedIsland();
        ClampCollapsedPosition();
        UpdateCollapsedAnchorFromCurrentGeometry();
        SavePosition();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }
}
