using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Binding = System.Windows.Data.Binding;

namespace YoyoClawCompanion.Controls;

// Cache a shadow of the island's outline independently from its moving text.
// The hole removes the source shape itself, preserving the island's translucency.
public sealed class CachedIslandShadow : Grid
{
    private readonly Border _source = new() { IsHitTestVisible = false };
    private Border? _target;
    private Effect? _effect;
    private (Rect Host, Rect Island, double RadiusX, double RadiusY)? _clipKey;
    private bool _initialized, _enabled;
    private int _animationVersion;

    public CachedIslandShadow()
    {
        IsHitTestVisible = false;
        CacheMode = new BitmapCache();
        Children.Add(_source);
        Loaded += (_, _) => { if (_target is not null) _target.LayoutUpdated += UpdateClip; UpdateClip(this, EventArgs.Empty); };
        Unloaded += (_, _) => { if (_target is not null) _target.LayoutUpdated -= UpdateClip; };
        SizeChanged += (_, _) => UpdateClip(this, EventArgs.Empty);
    }

    internal void Attach(Border target, Effect effect)
    {
        _target = target;
        _effect = effect;
        foreach (var (destination, path) in new[]
        {
            (WidthProperty, "ActualWidth"), (HeightProperty, "ActualHeight"),
            (MarginProperty, "Margin"), (HorizontalAlignmentProperty, "HorizontalAlignment"),
            (VerticalAlignmentProperty, "VerticalAlignment"), (OpacityProperty, "Opacity"),
            (RenderTransformProperty, "RenderTransform"), (RenderTransformOriginProperty, "RenderTransformOrigin"),
            (Border.CornerRadiusProperty, "CornerRadius"), (Border.BackgroundProperty, "Background"),
            (Border.BorderBrushProperty, "BorderBrush"), (Border.BorderThicknessProperty, "BorderThickness")
        }) _source.SetBinding(destination, new Binding(path) { Source = target });
        SetBinding(VisibilityProperty, new Binding("Visibility") { Source = target });
    }

    internal void SetEnabled(bool enabled, bool animate)
    {
        if (_initialized && _enabled == enabled) return;
        _enabled = enabled;
        var version = ++_animationVersion;
        if (enabled) _source.Effect = _effect;
        var targetOpacity = enabled ? 1d : 0d;
        if (!animate || !_initialized)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = targetOpacity;
            if (!enabled) _source.Effect = null;
        }
        else
        {
            var animation = new DoubleAnimation(Opacity, targetOpacity, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            animation.Completed += (_, _) =>
            {
                if (version != _animationVersion) return;
                BeginAnimation(OpacityProperty, null);
                Opacity = targetOpacity;
                if (!_enabled) _source.Effect = null;
            };
            BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        _initialized = true;
    }

    private void UpdateClip(object? sender, EventArgs e)
    {
        if (_target is null || ActualWidth <= 0 || ActualHeight <= 0 || _target.ActualWidth <= 0 || _target.ActualHeight <= 0) return;
        try
        {
            var bounds = _target.TransformToVisual(this).TransformBounds(new Rect(_target.RenderSize));
            var radiusX = _target.CornerRadius.TopLeft * bounds.Width / _target.ActualWidth;
            var radiusY = _target.CornerRadius.TopLeft * bounds.Height / _target.ActualHeight;
            var key = (new Rect(RenderSize), bounds, radiusX, radiusY);
            if (_clipKey == key) return; // Moving text leaves this cached outline untouched.
            _clipKey = key;
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(key.Item1), new RectangleGeometry(bounds, radiusX, radiusY));
            clip.Freeze();
            Clip = clip;
        }
        catch (InvalidOperationException) { } // Disconnected during window teardown.
    }
}
