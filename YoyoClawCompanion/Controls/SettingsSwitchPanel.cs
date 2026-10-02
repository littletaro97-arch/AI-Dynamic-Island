using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CheckBox = System.Windows.Controls.CheckBox;
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Panel = System.Windows.Controls.Panel;
using Cursors = System.Windows.Input.Cursors;

namespace YoyoClawCompanion.Controls;

// Stable slots let a hovered card cover just the next slot without reflowing the section.
public sealed class SettingsSwitchPanel : Panel
{
    private const string DragFormat = "AI.DynamicIsland.SettingCard";
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private FrameworkElement? _candidate;
    private Point _down;
    private FrameworkElement? _covered;
    private double _coveredOpacity;
    public bool IsEditing { get; private set; }
    public event EventHandler? EditRequested;
    public event EventHandler? OrderChanged;
    public IEnumerable<string> Order => InternalChildren.Cast<FrameworkElement>().Select(Key);

    public SettingsSwitchPanel()
    {
        AllowDrop = true;
        ClipToBounds = false;
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if (_candidate is null || Mouse.LeftButton != MouseButtonState.Pressed) return;
            EditRequested?.Invoke(this,EventArgs.Empty);
        };
        PreviewMouseLeftButtonDown += OnDown;
        PreviewMouseLeftButtonUp += (_,e) =>
        {
            _hold.Stop(); _candidate = null;
            if (IsEditing) { e.Handled = true; Mouse.Capture(null); }
        };
        PreviewMouseMove += OnMove;
        MouseLeave += (_,_) => { if (!IsEditing) { _hold.Stop(); _candidate = null; } };
        LostMouseCapture += (_,_) => _hold.Stop();
        PreviewDragOver += OnDragOver;
        PreviewDrop += OnDrop;
        Unloaded += (_,_) => { _hold.Stop(); SetEditing(false); };
    }

    public static string Key(FrameworkElement child) => child is ExpandableSettingCard card ? card.Header.Name : child.Name;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 600 : available.Width;
        foreach (UIElement child in InternalChildren) child.Measure(new Size(width/2,double.PositiveInfinity));
        return new Size(width,Math.Ceiling(InternalChildren.Count/2d)*72);
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (int i=0;i<InternalChildren.Count;i++)
        {
            var child = (FrameworkElement)InternalChildren[i];
            child.Arrange(new Rect(i%2*final.Width/2,i/2*72,final.Width/2,
                child is ExpandableSettingCard card ? card.Height+10 : 72));
        }
        return final;
    }

    public void ApplyOrder(IEnumerable<string> order)
    {
        var items = Children.Cast<FrameworkElement>().ToArray();
        var sorted = order.Distinct().Select(key => items.FirstOrDefault(item => Key(item)==key)).OfType<FrameworkElement>().ToList();
        sorted.AddRange(items.Where(item => !sorted.Contains(item)));
        Children.Clear();
        foreach (var item in sorted) Children.Add(item);
    }

    public void SetEditing(bool editing)
    {
        IsEditing = editing;
        foreach (FrameworkElement item in InternalChildren)
        {
            if (item is ExpandableSettingCard card) card.SetExpanded(false);
            item.Cursor = editing ? Cursors.SizeAll : null;
            var opacity = (double)item.GetAnimationBaseValue(OpacityProperty);
            item.BeginAnimation(OpacityProperty,new DoubleAnimation(editing ? opacity*.8 : opacity,TimeSpan.FromMilliseconds(180)));
        }
    }

    public void Expand(ExpandableSettingCard card, bool expanded)
    {
        if (expanded)
            foreach (var other in InternalChildren.OfType<ExpandableSettingCard>().Where(other => other != card)) other.SetExpanded(false);
        if (!expanded && _covered is null) return;
        RestoreCovered();
        if (!expanded) return;
        var indexOfCard = InternalChildren.IndexOf(card);
        var next = indexOfCard+2;
        if (next < InternalChildren.Count) _covered = (FrameworkElement)InternalChildren[next];
        else if (indexOfCard/2 == (InternalChildren.Count-1)/2 && Parent is StackPanel parent)
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
        _candidate = ItemAt(e.OriginalSource as DependencyObject);
        if (_candidate is null) return;
        if (_candidate is ExpandableSettingCard card && e.GetPosition(card).Y > 62) { _candidate = null; return; }
        _down = e.GetPosition(this);
        if (IsEditing) e.Handled = true;
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
        if (delta.Length < 6) return;
        var item = _candidate;
        _candidate = null;
        Mouse.Capture(null);
        try { DragDrop.DoDragDrop(item,new DataObject(DragFormat,item),DragDropEffects.Move); }
        finally { _hold.Stop(); }
    }

    private bool Accept(DragEventArgs e) => IsEditing && e.Data.GetData(DragFormat) is FrameworkElement item && Children.Contains(item);
    private void OnDragOver(object sender,DragEventArgs e) { e.Effects = Accept(e) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }
    private void OnDrop(object sender,DragEventArgs e)
    {
        e.Handled = true;
        if (!Accept(e)) return;
        var item = (FrameworkElement)e.Data.GetData(DragFormat);
        var position = e.GetPosition(this);
        var target = Math.Clamp((int)(position.Y/72)*2+(position.X<ActualWidth/2 ? 0 : 1),0,Children.Count-1);
        MoveItem(item,target);
    }

    public void MoveItem(FrameworkElement item,int target)
    {
        var from = Children.IndexOf(item);
        if (!IsEditing || from < 0 || target < 0 || target >= Children.Count || from == target) return;
        var old = Children.Cast<FrameworkElement>().ToDictionary(child => child,child => child.TranslatePoint(new Point(),this));
        Children.RemoveAt(from); Children.Insert(target,item);
        UpdateLayout();
        foreach (FrameworkElement child in Children)
        {
            var current = child.TranslatePoint(new Point(),this);
            var delta = old[child]-current;
            var transform = new TranslateTransform(); child.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(delta.X,0,TimeSpan.FromMilliseconds(220)));
            transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(delta.Y,0,TimeSpan.FromMilliseconds(220)));
        }
        OrderChanged?.Invoke(this,EventArgs.Empty);
    }
}
