using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CheckBox = System.Windows.Controls.CheckBox;
using Panel = System.Windows.Controls.Panel;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace YoyoClawCompanion.Controls;

public sealed class ExpandableSettingCard : Border
{
    public FrameworkElement Header => (FrameworkElement)((Panel)Child).Children[0];
    public FrameworkElement Detail => (FrameworkElement)((Panel)Child).Children[1];
    public bool IsExpanded { get; private set; }
    public bool ExpandWhenUnchecked { get; set; }
    public bool AutoSizeDetail { get; set; }
    private int _transition;
    private bool _headerConnected;

    public ExpandableSettingCard()
    {
        Height = 62;
        Margin = new Thickness(5);
        CornerRadius = new CornerRadius(16);
        BorderThickness = new Thickness(1.5);
        Background = new SolidColorBrush(Color.FromArgb(8,62,213,152));
        BorderBrush = new SolidColorBrush(Color.FromArgb(48,62,213,152));
        Loaded += (_, _) =>
        {
            if (_headerConnected) return;
            _headerConnected = true;
            if (Header is CheckBox toggle) { toggle.Checked += HeaderChanged; toggle.Unchecked += HeaderChanged; }
            Header.IsEnabledChanged += HeaderEnabledChanged;
            SetTint(false);
        };
        Unloaded += (_, _) =>
        {
            if (_headerConnected)
            {
                if (Header is CheckBox toggle) { toggle.Checked -= HeaderChanged; toggle.Unchecked -= HeaderChanged; }
                Header.IsEnabledChanged -= HeaderEnabledChanged;
                _headerConnected = false;
            }
            ++_transition;
            IsExpanded = false;
            BeginAnimation(HeightProperty, null); Height = 62;
            BeginAnimation(OpacityProperty, null);
            Detail.BeginAnimation(OpacityProperty, null); Detail.Opacity = 0; Detail.IsHitTestVisible = false;
            SetTint(false);
            Panel.SetZIndex(this, 0);
            (Parent as SettingsSwitchPanel)?.Expand(this, false);
        };
        MouseEnter += (_, _) => SetExpanded(true);
        MouseLeave += (_, _) => SetExpanded(false);
        SizeChanged += (_, args) => { if (args.WidthChanged && IsExpanded && AutoSizeDetail) AnimateHeight(); };
    }

    private void HeaderEnabledChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if (!Header.IsEnabled) SetExpanded(false);
    }

    private void HeaderChanged(object sender, RoutedEventArgs e)
    {
        SetTint(true);
        SetExpanded(IsMouseOver);
    }

    private void SetTint(bool animate)
    {
        var enabled = Header is CheckBox toggle ? toggle.IsChecked == true : IsExpanded;
        foreach (var (brush, alpha) in new[] { ((SolidColorBrush)Background, enabled ? (byte)18 : (byte)8), ((SolidColorBrush)BorderBrush, enabled ? (byte)98 : (byte)48) })
        {
            var color = Color.FromArgb(alpha,62,213,152);
            var previous = brush.Color;
            brush.BeginAnimation(SolidColorBrush.ColorProperty,null);
            brush.Color = color;
            if (animate) brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(previous,color,TimeSpan.FromMilliseconds(180)));
        }
    }

    public void SetExpanded(bool expanded)
    {
        expanded &= Header.IsEnabled && (Header is not CheckBox toggle || toggle.IsChecked == true || ExpandWhenUnchecked)
            && (Parent as SettingsSwitchPanel)?.IsEditing != true;
        if (IsExpanded == expanded) return;
        IsExpanded = expanded;
        var version = ++_transition;
        AnimateHeight();
        if (Header is not CheckBox) SetTint(true);
        var fade = new DoubleAnimation(Detail.Opacity,expanded ? 1 : 0,TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { if (version == _transition) Detail.IsHitTestVisible = expanded; };
        Detail.IsHitTestVisible = expanded;
        Detail.BeginAnimation(OpacityProperty,fade);
        Panel.SetZIndex(this,expanded ? 10 : 0);
        (Parent as SettingsSwitchPanel)?.Expand(this,expanded);
    }

    private void AnimateHeight()
    {
        var target = IsExpanded ? 126d : 62d;
        if (IsExpanded && AutoSizeDetail)
        {
            Detail.Measure(new Size(Math.Max(1, ActualWidth - BorderThickness.Left - BorderThickness.Right), double.PositiveInfinity));
            target = Math.Max(target, 62 + Detail.DesiredSize.Height + 4);
        }
        var previous = Height;
        if (Math.Abs(previous - target) < .1) return;
        BeginAnimation(HeightProperty, null);
        Height = target;
        BeginAnimation(HeightProperty, new DoubleAnimation(previous, target, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
}
