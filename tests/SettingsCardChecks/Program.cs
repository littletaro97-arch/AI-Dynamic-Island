using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using YoyoClawCompanion;
using YoyoClawCompanion.Controls;

internal static class Program
{
    sealed class ControlTestApp : App
    {
        public ControlTestApp() => typeof(App).GetProperty("IsPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        protected override void OnStartup(StartupEventArgs e) { }
    }
    const BindingFlags Flags = BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static void Assert(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); }
    static void Settle()
    {
        var frame=new DispatcherFrame(); var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(300)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    static void Layout(FrameworkElement root) { root.Measure(new Size(920,720));root.Arrange(new Rect(0,0,920,720));root.UpdateLayout(); }
    [STAThread] static void Main()
    {
        var app=new ControlTestApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var main=new MainWindow();
        var window=(SettingsWindow)Activator.CreateInstance(typeof(SettingsWindow),Flags,null,[main],null)!;
        typeof(SettingsWindow).GetField("_loading",Flags)!.SetValue(window,true);
        var root=(FrameworkElement)window.Content;Layout(root);
        var feature=(Border)window.FindName("FeatureCard");var notification=(Border)window.FindName("NotificationCard");
        var a=(TextBlock)((StackPanel)feature.Child).Children[0];var b=(TextBlock)((StackPanel)notification.Child).Children[0];
        Assert(a.Style==b.Style && a.FontWeight==b.FontWeight && a.FontSize==b.FontSize,"section heading typography identical");
        var action = (ExpandableSettingCard)window.FindName("NotificationBannerSettingsCard");
        var actionPanel = (SettingsSwitchPanel)action.Parent;
        Assert(action.Header is Button && actionPanel.Columns == 1, "system settings entry reuses full width expandable card without a fake switch");
        var guidance = (TextBlock)window.FindName("NotificationBannerGuidance");
        var lines = (FrameworkElement)window.FindName("MaxResponseLinesCombo");
        var linePosition = lines.TranslatePoint(new Point(), root);
        action.SetExpanded(true); Settle(); Layout(root);
        Assert(action.Height >= 126 && action.Detail.Opacity == 1 && guidance.IsDescendantOf(action), "hover description is enclosed by shared animated card");
        Assert(action.Detail.TransformToAncestor(action).TransformBounds(new Rect(action.Detail.RenderSize)).Bottom <= action.ActualHeight, "description remains inside green outline");
        var actionNext = ((StackPanel)actionPanel.Parent).Children[((StackPanel)actionPanel.Parent).Children.IndexOf(actionPanel) + 1];
        Assert(actionNext.Opacity == 0 && !actionNext.IsHitTestVisible && lines.TranslatePoint(new Point(), root) == linePosition, "full width description only covers next row while later controls stay fixed");
        action.SetExpanded(false); action.SetExpanded(true); action.SetExpanded(false); Settle(); Layout(root);
        Assert(action.Height == 62 && action.Detail.Opacity == 0 && actionNext.Opacity == 1 && actionNext.IsHitTestVisible, "rapid description reversal restores next row and compact header");
        var fullscreen = (ExpandableSettingCard)window.FindName("FullscreenActiveOnlyCard");
        ((CheckBox)fullscreen.Header).IsChecked = false; fullscreen.SetExpanded(true); Settle(); Layout(root);
        Assert(fullscreen.IsExpanded && !((CheckBox)fullscreen.Header).IsChecked.GetValueOrDefault(), "explanation hover is available without toggling fullscreen behaviour");
        ((SettingsSwitchPanel)fullscreen.Parent).SetEditing(true); fullscreen.SetExpanded(true);
        Assert(!fullscreen.IsExpanded, "description cards preserve edit mode restrictions");
        ((SettingsSwitchPanel)fullscreen.Parent).SetEditing(false); Settle();
        var reverseDescription = (ExpandableSettingCard)window.FindName("ReverseHoverCard");
        ((CheckBox)reverseDescription.Header).IsChecked = false;
        reverseDescription.SetExpanded(true); Settle(); Layout(root);
        Assert(reverseDescription.IsExpanded && reverseDescription.Detail.Opacity == 1, "reverse hover description reuses expandable card even when disabled");
        Assert(reverseDescription.Detail.TransformToAncestor(reverseDescription).TransformBounds(new Rect(reverseDescription.Detail.RenderSize)).Bottom <= reverseDescription.ActualHeight, "Alt explanation remains enclosed at minimum font size");
        if (Environment.GetEnvironmentVariable("ISLAND_LAYOUT_EVIDENCE_DIR") is { Length: > 0 } altEvidence)
        {
            Directory.CreateDirectory(altEvidence);
            var originalPanel = (SettingsSwitchPanel)reverseDescription.Parent;
            var originalIndex = originalPanel.Children.IndexOf(reverseDescription);
            originalPanel.Children.Remove(reverseDescription);
            var captureWindow = new Window { Content = reverseDescription, Width = 310, SizeToContent = SizeToContent.Height,
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                Background = feature.Background, Foreground = window.Foreground, FontSize = 12 };
            captureWindow.Resources.MergedDictionaries.Add(window.Resources);
            captureWindow.Show(); reverseDescription.SetExpanded(true); Settle(); captureWindow.UpdateLayout();
            SaveVisual(reverseDescription, Path.Combine(altEvidence, "reverse-alt-description.png"), feature.Background);
            captureWindow.Close(); Settle(); captureWindow.Content = null;
            originalPanel.Children.Insert(originalIndex, reverseDescription);
        }
        reverseDescription.SetExpanded(false); Settle();
        foreach (var width in new[] { 780d, 920d })
        {
            root.Measure(new Size(width, 720)); root.Arrange(new Rect(0, 0, width, 720)); root.UpdateLayout();
            action.SetExpanded(true); Settle(); root.UpdateLayout();
            var batchGuidance = (FrameworkElement)window.FindName("NotificationBatchGuidance");
            var borderBottom = action.TranslatePoint(new Point(0, action.ActualHeight), root).Y;
            Assert(batchGuidance.TranslatePoint(new Point(), root).Y - borderBottom >= 6, "notification explanation clears expanded border by visible gap " + width);
            action.SetExpanded(false); Settle();
        }
        var referenceSize = ((TextBlock)window.FindName("SystemNotificationStatusText")).FontSize;
        Assert(((Slider)window.FindName("TextSizeSlider")).Minimum == referenceSize, "font slider minimum matches notification status size");
        bool TextSizesValid(DependencyObject parent)
        {
            if (parent is TextBlock text && text.FontSize < referenceSize) return false;
            if (parent is Control control && control.FontSize < referenceSize) return false;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (!TextSizesValid(VisualTreeHelper.GetChild(parent, index))) return false;
            return true;
        }
        Assert(TextSizesValid(root), "settings visual tree contains no text smaller than reference size");
        var rule = new YoyoClawCompanion.Services.NotificationTitleRule("fixture.app", "通知标题", "测试来源");
        main.CurrentSettings.IgnoredNotificationTitles = [rule];
        window.Left = window.Top = -10000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
        var filterButton = (Button)window.FindName("NotificationFiltersButton");
        foreach (var mode in new[] { "light", "dark" })
        {
            var themes = (ComboBox)window.FindName("ThemeCombo");
            themes.SelectedItem = themes.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == mode);
            typeof(SettingsWindow).GetMethod("ApplyPanelTheme", Flags)!.Invoke(window, null);
            typeof(SettingsWindow).GetMethod("NotificationFilters_Click", Flags)!.Invoke(window, [filterButton, new RoutedEventArgs(Button.ClickEvent)]);
            Settle();
            var menu = filterButton.ContextMenu!; menu.ApplyTemplate();
            var surface = (Border)VisualTreeHelper.GetChild(menu, 0);
            var item = (MenuItem)menu.Items[0]; item.ApplyTemplate();
            Assert(surface.Style == window.FindResource("SettingsPopupSurface") && surface.CornerRadius.TopLeft == 9, "blocked notification menu shares dropdown surface " + mode);
            Assert(item.Style == window.FindResource("SettingsContextMenuItem") && item.Tag == rule, "styled restore action retains exact rule " + mode);
            Assert(((SolidColorBrush)surface.Background).Color == ((SolidColorBrush)window.Resources["SettingsPopupBackground"]).Color && menu.Foreground == window.Resources["SettingsHintForeground"], "popup honours current theme " + mode);
            if (Environment.GetEnvironmentVariable("ISLAND_LAYOUT_EVIDENCE_DIR") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                SaveVisual(menu, Path.Combine(evidence, "blocked-notifications-" + mode + ".png"));
                action.SetExpanded(true); Settle(); window.UpdateLayout();
                SaveVisual(action, Path.Combine(evidence, "notification-description-" + mode + ".png"), notification.Background);
                action.SetExpanded(false); Settle();
            }
            menu.IsOpen = false;
        }
        main.CurrentSettings.IgnoredNotificationTitles = [];
        ((ComboBox)window.FindName("ThemeCombo")).SelectedIndex = 1;
        typeof(SettingsWindow).GetMethod("ApplyPanelTheme", Flags)!.Invoke(window, null);
        Layout(root);
        var card=(ExpandableSettingCard)window.FindName("CodexResetReminderCard");
        var hidden=(ExpandableSettingCard)window.FindName("UnchangedAutoHideCard");
        var panel=(SettingsSwitchPanel)card.Parent;
        ((CheckBox)card.Header).IsChecked=false;card.SetExpanded(true);Settle();Layout(root);
        Assert(!card.IsExpanded && card.Height==62,"disabled setting stays collapsed");
        card.Header.IsEnabled=true;((CheckBox)card.Header).IsChecked=true;
        var positions=panel.Children.Cast<FrameworkElement>().Select(item=>item.TranslatePoint(new Point(),panel)).ToArray();
        var after=(FrameworkElement)window.FindName("YoyoLaunchForCheckinCheck");
        var originalOpacity=after.Opacity;
        card.SetExpanded(true);Settle();Layout(root);
        Assert(card.Height==126 && card.Detail.ActualWidth>0 && card.Detail.Opacity==1,"enabled setting expands detail inside enclosing card");
        Assert(card.Detail.TransformToAncestor(card).TransformBounds(new Rect(card.Detail.RenderSize)).Bottom<=card.ActualHeight,"green border contains slider detail");
        Assert(after.Opacity==0 && !after.IsHitTestVisible,"only next card fades and cannot intercept slider");
        for(int i=0;i<panel.Children.Count;i++)
            if(i!=4) Assert(((FrameworkElement)panel.Children[i]).TranslatePoint(new Point(),panel)==positions[i],"other slot stays fixed "+i);
        card.SetExpanded(false);card.SetExpanded(true);card.SetExpanded(false);Settle();Layout(root);
        Assert(card.Height==62 && card.Detail.Opacity==0 && after.Opacity==originalOpacity && after.IsHitTestVisible,"rapid hover reversal fully restores next card");
        card.SetExpanded(true);panel.SetEditing(true);Settle();Layout(root);
        Assert(!card.IsExpanded && card.Height==62,"editing collapses detail");
        card.SetExpanded(true);Assert(!card.IsExpanded,"editing blocks hover expansion");
        panel.SetEditing(false);Settle();
        foreach (var name in new[]{"HoverExpansionCard","CompletionNotificationsCard"})
        {
            var combined=(ExpandableSettingCard)window.FindName(name);
            ((CheckBox)combined.Header).IsChecked=false;combined.SetExpanded(true);Settle();
            Assert(!combined.IsExpanded,name+" off stays collapsed");
            ((CheckBox)combined.Header).IsChecked=true;combined.Header.IsEnabled=true;combined.SetExpanded(true);Settle();Layout(root);
            Assert(combined.IsExpanded && combined.Detail.ActualHeight>0 && combined.Detail.Opacity==1,name+" merges slider into card");
            Assert(combined.Detail.IsDescendantOf(combined),name+" slider remains within outer border");
            if(name=="HoverExpansionCard")
            {
                var fullWidth=(FrameworkElement)window.FindName("QuotaScrollSpeedPanel");
                var scrollSlider=(FrameworkElement)window.FindName("QuotaScrollSpeedSlider");
                Assert(fullWidth.ActualWidth>combined.ActualWidth && scrollSlider.IsDescendantOf(fullWidth),"wide following setting is one complete container");
                Assert(fullWidth.Opacity==0 && !fullWidth.IsHitTestVisible,"wide following label and slider fade together");
            }
            ((SettingsSwitchPanel)combined.Parent).SetEditing(true);combined.SetExpanded(true);Settle();
            Assert(!combined.IsExpanded,name+" editing blocks expansion");
            ((SettingsSwitchPanel)combined.Parent).SetEditing(false);
            if(name=="HoverExpansionCard")
            {
                Settle();
                Assert(((FrameworkElement)window.FindName("QuotaScrollSpeedPanel")).Opacity==1,"wide following setting restores after collapse");
            }
        }
        var hints=(System.Collections.IEnumerable)typeof(SettingsWindow).GetField("_settingHints",Flags)!.GetValue(window)!;
        foreach(var hint in hints)
        {
            var owner=(FrameworkElement)hint.GetType().GetField("_owner",Flags)!.GetValue(hint)!;
            Assert(owner.Parent is not Grid grid || grid.Parent is not ExpandableSettingCard,"combined cards have no long-press hint");
        }
        foreach(var name in new[]{"CodexActivityCheckSettingsPanel","CompletionNotificationsCheckSettingsPanel"})
            Assert(((FrameworkElement)window.FindName(name)).ToolTip is null,"no sticky panel-wide hint "+name);
        // Standalone panel tests avoid saving the user's real settings.
        var sortable=new SettingsSwitchPanel();
        var one=new CheckBox{Name="One"};var two=new CheckBox{Name="Two"};var three=new CheckBox{Name="Three"};
        sortable.Children.Add(one);sortable.Children.Add(two);sortable.Children.Add(three);Layout(sortable);
        sortable.SetEditing(true);sortable.MoveItem(one,2);Settle();
        Assert(sortable.Order.SequenceEqual(new[]{"Two","Three","One"}),"same section reorder");
        var grab = typeof(SettingsSwitchPanel).GetMethod("SetGrabbed",Flags)!;
        var release = typeof(SettingsSwitchPanel).GetMethod("ReleaseGrabbed",Flags)!;
        grab.Invoke(sortable,[one]);Settle();
        var grabbedScale = (ScaleTransform)((TransformGroup)one.RenderTransform).Children[0];
        Assert(Math.Abs(grabbedScale.ScaleX-.94)<.001 && Math.Abs(grabbedScale.ScaleY-.94)<.001,"selected card shrinks symmetrically when grabbed");
        sortable.MoveItem(one,0);Settle();
        Assert(Math.Abs(grabbedScale.ScaleX-.94)<.001,"reorder preserves grabbed scale independently of translation");
        release.Invoke(sortable,null);grab.Invoke(sortable,[one]);release.Invoke(sortable,null);Settle();
        Assert(grabbedScale.ScaleX==1 && grabbedScale.ScaleY==1,"rapid grab release restores scale");
        grab.Invoke(sortable,[one]);sortable.SetEditing(false);Settle();
        Assert(grabbedScale.ScaleX==1,"leaving edit mode releases grabbed card");
        sortable.SetEditing(true);Settle();
        typeof(SettingsSwitchPanel).GetField("_down",Flags)!.SetValue(sortable,new Point(10,10));
        grab.Invoke(sortable,[one]);Settle();
        var translation=(TranslateTransform)((TransformGroup)one.RenderTransform).Children[1];
        typeof(SettingsSwitchPanel).GetMethod("UpdateGrabPosition",Flags)!.Invoke(sortable,[new Point(80,45)]);
        Assert(Math.Abs(translation.X-70)<.01 && Math.Abs(translation.Y-35)<.01 && Panel.GetZIndex(one)>Panel.GetZIndex(two),"grabbed card follows pointer above other cards");
        typeof(SettingsSwitchPanel).GetMethod("UpdateGrabPosition",Flags)!.Invoke(sortable,[new Point(110,60)]);
        Assert(Math.Abs(translation.X-100)<.01 && Math.Abs(translation.Y-50)<.01,"pointer movement updates without accumulating drift");
        sortable.SetEditing(false);Settle();
        Assert(translation.X==0 && translation.Y==0 && grabbedScale.ScaleX==1,"cancelled drag returns card to its slot");
        sortable.SetEditing(true);Settle();
        grab.Invoke(sortable,[one]);Settle();
        typeof(SettingsSwitchPanel).GetMethod("UpdateGrabPosition",Flags)!.Invoke(sortable,[new Point(850,30)]);
        typeof(SettingsSwitchPanel).GetMethod("FinishPointerDrag",Flags)!.Invoke(sortable,[new Point(850,30),true]);Settle();
        Assert(sortable.Order.SequenceEqual(new[]{"Two","One","Three"}) && translation.X==0 && translation.Y==0,"drop into same section reorders and settles without a jump");
        sortable.SetEditing(true);sortable.MoveItem(one,2);Settle();
        sortable.MoveItem(new CheckBox{Name="Outside"},0);
        Assert(sortable.Order.SequenceEqual(new[]{"Two","Three","One"}),"cross section move rejected");
        sortable.ApplyOrder(new[]{"One","Removed","One","Two"});
        Assert(sortable.Order.SequenceEqual(new[]{"One","Two","Three"}),"saved order tolerates duplicate and removed keys");
        ((CheckBox)hidden.Header).IsChecked=true;hidden.Header.IsEnabled=true;hidden.SetExpanded(true);Settle();Layout(root);
        Assert(((FrameworkElement)window.FindName("MaxResponseLinesCombo")).Opacity==1,"following controls keep their own state");
        action.SetExpanded(true); Settle(); window.Close(); Settle();
        Assert(!action.IsExpanded && action.Height == 62 && !action.HasAnimatedProperties && !action.Detail.HasAnimatedProperties, "unloaded descriptions release animation clocks");
        main.Close();app.Shutdown();
    }

    static void SaveVisual(FrameworkElement element, string path, Brush? background = null)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 1.5), (int)Math.Ceiling(element.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        if (background is not null)
        {
            var surface = new DrawingVisual();
            using (var drawing = surface.RenderOpen()) drawing.DrawRectangle(background, null, new Rect(element.RenderSize));
            bitmap.Render(surface);
        }
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
