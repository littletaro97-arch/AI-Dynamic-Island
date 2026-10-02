using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CheckBox = System.Windows.Controls.CheckBox;
using Panel = System.Windows.Controls.Panel;
using Color = System.Windows.Media.Color;

namespace YoyoClawCompanion.Controls;

public sealed class ExpandableSettingCard : Border
{
    public CheckBox Header => (CheckBox)((Panel)Child).Children[0];
    public FrameworkElement Detail => (FrameworkElement)((Panel)Child).Children[1];
    public bool IsExpanded { get; private set; }
    private int _transition;

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
            Header.Checked += HeaderChanged;
            Header.Unchecked += HeaderChanged;
            Header.IsEnabledChanged += HeaderEnabledChanged;
            SetTint(false);
        };
        Unloaded += (_, _) => { Header.Checked -= HeaderChanged; Header.Unchecked -= HeaderChanged; Header.IsEnabledChanged -= HeaderEnabledChanged; };
        MouseEnter += (_, _) => SetExpanded(true);
        MouseLeave += (_, _) => SetExpanded(false);
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
        var enabled = Header.IsChecked == true;
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
        expanded &= Header.IsChecked == true && Header.IsEnabled && (Parent as SettingsSwitchPanel)?.IsEditing != true;
        if (IsExpanded == expanded) return;
        IsExpanded = expanded;
        var version = ++_transition;
        var height = Height;
        BeginAnimation(HeightProperty,null);
        Height = expanded ? 126 : 62;
        BeginAnimation(HeightProperty,new DoubleAnimation(height,Height,TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        var fade = new DoubleAnimation(Detail.Opacity,expanded ? 1 : 0,TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { if (version == _transition) Detail.IsHitTestVisible = expanded; };
        Detail.IsHitTestVisible = expanded;
        Detail.BeginAnimation(OpacityProperty,fade);
        Panel.SetZIndex(this,expanded ? 10 : 0);
        (Parent as SettingsSwitchPanel)?.Expand(this,expanded);
    }
}
