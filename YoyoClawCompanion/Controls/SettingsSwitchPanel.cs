using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CheckBox = System.Windows.Controls.CheckBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Panel = System.Windows.Controls.Panel;
using Cursors = System.Windows.Input.Cursors;

namespace YoyoClawCompanion.Controls;

// Stable slots let a hovered card cover just the next slot without reflowing the section.
public sealed class SettingsSwitchPanel : Panel
{
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private FrameworkElement? _candidate;
    private FrameworkElement? _grabbed;
    private Point _down;
    private Vector _grabStartTranslation;
    private bool _dragMoved, _finishingDrag;
    private FrameworkElement? _covered;
    private double _coveredOpacity;
    public bool IsEditing { get; private set; }
    public int Columns { get; set; } = 2;
    private int ColumnCount => Math.Max(1, Columns);
    public event EventHandler? EditRequested;
    public event EventHandler? OrderChanged;
    public IEnumerable<string> Order => InternalChildren.Cast<FrameworkElement>().Select(Key);

    public SettingsSwitchPanel()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        ClipToBounds = false;
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if (_candidate is null || Mouse.LeftButton != MouseButtonState.Pressed) return;
            EditRequested?.Invoke(this,EventArgs.Empty);
            if (IsEditing) { SetGrabbed(_candidate); CaptureMouse(); }
        };
        PreviewMouseLeftButtonDown += OnDown;
        PreviewMouseLeftButtonUp += (_,e) =>
        {
            _hold.Stop();
            if (IsEditing) { FinishPointerDrag(e.GetPosition(this)); e.Handled = true; }
            else _candidate = null;
        };
        PreviewMouseMove += OnMove;
        MouseLeave += (_,_) => { if (!IsEditing) { _hold.Stop(); _candidate = null; } };
        LostMouseCapture += (_,e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, this)) return;
            _hold.Stop();
            if (!_finishingDrag) FinishPointerDrag(_down, false);
        };
        Unloaded += (_,_) => { _hold.Stop(); SetEditing(false); };
    }

    public static string Key(FrameworkElement child) => child is ExpandableSettingCard card ? card.Header.Name : child.Name;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 600 : available.Width;
        foreach (UIElement child in InternalChildren) child.Measure(new Size(width/ColumnCount,double.PositiveInfinity));
        return new Size(width,Math.Ceiling(InternalChildren.Count/(double)ColumnCount)*72);
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (int i=0;i<InternalChildren.Count;i++)
        {
            var child = (FrameworkElement)InternalChildren[i];
            child.Arrange(new Rect(i%ColumnCount*final.Width/ColumnCount,i/ColumnCount*72,final.Width/ColumnCount,
                child is ExpandableSettingCard card ? card.Height+10 : 72));
        }
        return final;
    }

    public void ApplyOrder(IEnumerable<string> order)
    {
        var items = Children.Cast<FrameworkElement>().ToArray();
        foreach (var card in items.OfType<ExpandableSettingCard>()) card.SetExpanded(false);
        RestoreCovered();
        var sorted = order.Distinct().Select(key => items.FirstOrDefault(item => Key(item)==key)).OfType<FrameworkElement>().ToList();
        sorted.AddRange(items.Where(item => !sorted.Contains(item)));
        Children.Clear();
        foreach (var item in sorted) Children.Add(item);
    }

    public void SetEditing(bool editing)
    {
        if (IsEditing == editing) { if (!editing) FinishPointerDrag(_down, false); return; }
        IsEditing = editing;
        if (!editing) FinishPointerDrag(_down, false);
        foreach (FrameworkElement item in InternalChildren)
        {
            if (item is ExpandableSettingCard card) card.SetExpanded(false);
            item.Cursor = editing ? Cursors.SizeAll : null;
            var opacity = (double)item.GetAnimationBaseValue(OpacityProperty);
            item.BeginAnimation(OpacityProperty,new DoubleAnimation(editing ? opacity*.8 : opacity,TimeSpan.FromMilliseconds(180)));
        }
    }

    // Keep scale separate from the translation used by slot-reordering animations.
    private static TransformGroup CardTransform(FrameworkElement item)
    {
        if (item.RenderTransform is TransformGroup group && group.Children.Count == 2
            && group.Children[0] is ScaleTransform && group.Children[1] is TranslateTransform) return group;
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform());
        transforms.Children.Add(item.RenderTransform is TranslateTransform translation ? translation : new TranslateTransform());
        item.RenderTransformOrigin = new Point(.5, .5);
        item.RenderTransform = transforms;
        return transforms;
    }

    private void SetGrabbed(FrameworkElement? item)
    {
        if (item == _grabbed) return;
        ReleaseGrabbed();
        _grabbed = item;
        if (item is null) return;
        _dragMoved = false;
        var translation = (TranslateTransform)CardTransform(item).Children[1];
        _grabStartTranslation = new Vector(translation.X, translation.Y);
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.BeginAnimation(TranslateTransform.YProperty, null);
        translation.X = _grabStartTranslation.X;
        translation.Y = _grabStartTranslation.Y;
        SetZIndex(item, 100);
        AnimateGrab(item, .94);
    }

    private void ReleaseGrabbed()
    {
        var item = _grabbed;
        _grabbed = null;
        if (item is not null)
        {
            AnimateGrab(item, 1);
            var translation = (TranslateTransform)CardTransform(item).Children[1];
            foreach (var property in new[] { TranslateTransform.XProperty, TranslateTransform.YProperty })
            {
                var current = (double)translation.GetValue(property);
                translation.BeginAnimation(property, null);
                translation.SetValue(property, 0d);
                translation.BeginAnimation(property, new DoubleAnimation(current, 0, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            SetZIndex(item, 0);
        }
    }

    private static void AnimateGrab(FrameworkElement item, double scale)
    {
        var transform = (ScaleTransform)CardTransform(item).Children[0];
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            transform.BeginAnimation(property, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public void Expand(ExpandableSettingCard card, bool expanded)
    {
        if (expanded)
            foreach (var other in InternalChildren.OfType<ExpandableSettingCard>().Where(other => other != card)) other.SetExpanded(false);
        if (!expanded && _covered is null) return;
        RestoreCovered();
        if (!expanded) return;
        var indexOfCard = InternalChildren.IndexOf(card);
        var next = indexOfCard+ColumnCount;
        if (next < InternalChildren.Count) _covered = (FrameworkElement)InternalChildren[next];
        else if (indexOfCard/ColumnCount == (InternalChildren.Count-1)/ColumnCount && Parent is StackPanel parent)
        {
            var index = parent.Children.IndexOf(this)+1;
            if (index < parent.Children.Count) _covered = parent.Children[index] as FrameworkElement;
        }
        if (_covered is null) return;
        _coveredOpacity = (double)_covered.GetAnimationBaseValue(OpacityProperty);
        _covered.IsHitTestVisible = false;
        _covered.RenderTransform = new TranslateTransform();
        ((TranslateTransform)_covered.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(16,TimeSpan.FromMilliseconds(220)));
        _covered.BeginAnimation(OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(180)));
    }

    private void RestoreCovered()
    {
        var item = _covered;
        _covered = null;
        if (item is null) return;
        item.IsHitTestVisible = true;
        var transform = item.RenderTransform as TranslateTransform;
        transform?.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(220)));
        var fade = new DoubleAnimation(_coveredOpacity,TimeSpan.FromMilliseconds(180));
        item.BeginAnimation(OpacityProperty,fade);
    }

    private FrameworkElement? ItemAt(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && Children.Contains(element)) return element;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void OnDown(object sender,MouseButtonEventArgs e)
    {
        if (!IsEditing && EditRequested is null) return;
        _candidate = ItemAt(e.OriginalSource as DependencyObject);
        if (_candidate is null) return;
        if (_candidate is ExpandableSettingCard card && e.GetPosition(card).Y > 62) { _candidate = null; return; }
        _down = e.GetPosition(this);
        if (IsEditing) { SetGrabbed(_candidate); CaptureMouse(); e.Handled = true; }
        else _hold.Start();
    }

    private void OnMove(object sender,MouseEventArgs e)
    {
        if (_candidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this)-_down;
        if (!IsEditing)
        {
            if (delta.Length > 6) { _hold.Stop(); _candidate = null; }
            return;
        }
        UpdateGrabPosition(e.GetPosition(this));
        e.Handled = true;
    }

    // Render the picked-up card itself, so it stays attached to the pointer.
    // Native DoDragDrop only displayed a cursor and left the card in its slot.
    private void UpdateGrabPosition(Point position)
    {
        if (!IsEditing || _grabbed is null) return;
        var delta = position - _down;
        _dragMoved |= delta.Length >= 6;
        var translation = (TranslateTransform)CardTransform(_grabbed).Children[1];
        translation.X = _grabStartTranslation.X + delta.X;
        translation.Y = _grabStartTranslation.Y + delta.Y;
    }

    private void FinishPointerDrag(Point position, bool reorder = true)
    {
        if (_finishingDrag) return;
        _finishingDrag = true;
        try
        {
            _hold.Stop();
            if (reorder && IsEditing && _dragMoved && _grabbed is { } item
                && new Rect(RenderSize).Contains(position))
            {
                var column = Math.Clamp((int)(position.X / (ActualWidth / ColumnCount)), 0, ColumnCount - 1);
                var target = Math.Clamp((int)(position.Y/72)*ColumnCount+column,0,Children.Count-1);
                MoveItem(item,target);
            }
            _candidate = null;
            ReleaseGrabbed();
            if (IsMouseCaptured) ReleaseMouseCapture();
        }
        finally { _finishingDrag = false; }
    }

    public void MoveItem(FrameworkElement item,int target)
    {
        var from = Children.IndexOf(item);
        if (!IsEditing || from < 0 || target < 0 || target >= Children.Count || from == target) return;
        var old = Children.Cast<FrameworkElement>().ToDictionary(child => child,child => child.TranslatePoint(new Point(),this));
        foreach (FrameworkElement child in Children)
        {
            var translation = (TranslateTransform)CardTransform(child).Children[1];
            translation.BeginAnimation(TranslateTransform.XProperty, null);
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            translation.X = translation.Y = 0;
        }
        Children.RemoveAt(from); Children.Insert(target,item);
        UpdateLayout();
        foreach (FrameworkElement child in Children)
        {
            var current = child.TranslatePoint(new Point(),this);
            var delta = old[child]-current;
            var transform = (TranslateTransform)CardTransform(child).Children[1];
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(delta.X,0,TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
            transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(delta.Y,0,TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        }
        OrderChanged?.Invoke(this,EventArgs.Empty);
    }
}
