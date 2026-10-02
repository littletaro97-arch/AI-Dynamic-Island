using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YoyoClawCompanion;
using YoyoClawCompanion.Controls;

internal static class Program
{
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
        var app=new App();
        var main=new MainWindow();
        var window=(SettingsWindow)Activator.CreateInstance(typeof(SettingsWindow),Flags,null,[main],null)!;
        typeof(SettingsWindow).GetField("_loading",Flags)!.SetValue(window,true);
        var root=(FrameworkElement)window.Content;Layout(root);
        var feature=(Border)window.FindName("FeatureCard");var notification=(Border)window.FindName("NotificationCard");
        var a=(TextBlock)((StackPanel)feature.Child).Children[0];var b=(TextBlock)((StackPanel)notification.Child).Children[0];
        Assert(a.Style==b.Style && a.FontWeight==b.FontWeight && a.FontSize==b.FontSize,"section heading typography identical");
        var card=(ExpandableSettingCard)window.FindName("CodexResetReminderCard");
        var hidden=(ExpandableSettingCard)window.FindName("UnchangedAutoHideCard");
        var panel=(SettingsSwitchPanel)card.Parent;
        card.Header.IsChecked=false;card.SetExpanded(true);Settle();Layout(root);
        Assert(!card.IsExpanded && card.Height==62,"disabled setting stays collapsed");
        card.Header.IsEnabled=true;card.Header.IsChecked=true;
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
            combined.Header.IsChecked=false;combined.SetExpanded(true);Settle();
            Assert(!combined.IsExpanded,name+" off stays collapsed");
            combined.Header.IsChecked=true;combined.Header.IsEnabled=true;combined.SetExpanded(true);Settle();Layout(root);
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
        hidden.Header.IsChecked=true;hidden.Header.IsEnabled=true;hidden.SetExpanded(true);Settle();Layout(root);
        Assert(((FrameworkElement)window.FindName("MaxResponseLinesCombo")).Opacity==1,"following controls keep their own state");
        window.Close();main.Close();app.Shutdown();
    }
}
