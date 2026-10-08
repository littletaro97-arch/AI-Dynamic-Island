using System.Reflection;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using YoyoClawCompanion;
class Program
{
    sealed class ControlTestApp : App
    {
        public ControlTestApp() => typeof(App).GetProperty("IsPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        protected override void OnStartup(StartupEventArgs e) { }
    }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Call(object o,string method,params object[] args) => o.GetType().GetMethod(method,Flags)!.Invoke(o,args);
    static void Assert(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); }
    [STAThread] static void Main()
    {
        var app=new ControlTestApp { ShutdownMode=ShutdownMode.OnExplicitShutdown }; typeof(App).GetMethod("InitializeComponent",Flags)?.Invoke(app,null);
        var main=new MainWindow();
        var window=(Window)Activator.CreateInstance(typeof(SettingsWindow),Flags,null,new object[]{main},null)!;
        window.GetType().GetField("_loading",Flags)!.SetValue(window,true);
        var order=new[]{"Position","Appearance","Component","Feature","Notification","Preset","Update"};
        var nav=((Panel)window.FindName("NavigationItems")).Children.OfType<RadioButton>().Select(x=>x.Name).ToArray();
        Assert(nav.SequenceEqual(order.Select(x=>x+"Nav")),"navigation order");
        var cards=(Panel)((FrameworkElement)window.FindName("PositionCard")).Parent;
        Assert(cards.Children.OfType<Border>().Select(x=>x.Name).SequenceEqual(order.Select(x=>x+"Card")),"content order");
        Assert(((FrameworkElement)window.FindName("ReverseHoverCheck")).IsDescendantOf((DependencyObject)window.FindName("FeatureCard")),"reverse hover in behaviour");
        var settings=typeof(MainWindow).GetProperty("CurrentSettings",Flags)!.GetValue(main)!;
        foreach(var name in new[]{"SystemNotificationsCheck","CodexConfirmationNotificationsCheck"})
            Assert(((CheckBox)window.FindName(name)).Tag is System.Windows.Media.ImageSource, "notification icon is renderable " + name);
        Assert(ReferenceEquals(((CheckBox)window.FindName("CodexConfirmationNotificationsCheck")).Tag, ((CheckBox)window.FindName("CodexCheck")).Tag), "Codex waiting reuses provider icon");
        Assert(((Button)window.FindName("NotificationBannerSettingsButton")).IsDescendantOf((DependencyObject)window.FindName("NotificationCard")), "system settings entry stays in reminders");
        Assert((string)typeof(SettingsWindow).GetField("NotificationSettingsUri",BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue()! == "ms-settings:notifications", "entry targets Windows notifications without changing OS configuration");
        var reverse=settings.GetType().GetProperty("EnableReverseHover")!;
        var hover=settings.GetType().GetProperty("EnableHoverExpansion")!;
        reverse.SetValue(settings,true); hover.SetValue(settings,true); Call(settings,"NormalizeInteraction");
        Assert((bool)hover.GetValue(settings)! && (bool)reverse.GetValue(settings)!,"both hover modes survive normalization independently");
        for(int mask=0;mask<8;mask++) {
            string[] keys={"yoyo","codex","workbuddy"}; string[] fields={"_yoyoInstalled","_codexInstalled","_workBuddyInstalled"};
            for(int i=0;i<3;i++) typeof(MainWindow).GetField(fields[i],Flags)!.SetValue(main,(mask&(1<<i))!=0);
            Call(window,"UpdateDependencyStates"); Call(window,"LoadProviderOrder","yoyo,codex,workbuddy");
            var list=((IEnumerable)window.GetType().GetProperty("ProviderOrderItems")!.GetValue(window)!).Cast<object>().Select(x=>(string)x.GetType().GetProperty("Key")!.GetValue(x)!).ToArray();
            Assert(list.SequenceEqual(keys.Where((_,i)=>(mask&(1<<i))!=0)),"installed ordering mask "+mask);
            foreach(var (name,bit) in new[]{("YoyoCheck",0),("YoyoCreditsCheck",0),("YoyoAutoCheckinCheck",0),("CodexCheck",1),("CodexActivityCheck",1),("CodexLimitsCheck",1),("CodexResetReminderCheck",1),("WorkBuddyCheck",2),("WorkBuddyCreditsCheck",2),("ConfirmationNotificationsCheck",2)})
                if(((UIElement)window.FindName(name)).IsEnabled != ((mask&(1<<bit))!=0)) throw new Exception("enabled state "+name+" mask "+mask);
        }
        var hoverDelay=settings.GetType().GetProperty("HoverDelayMs")!;
        foreach(var (input,expected) in new[]{(400d,130d),(0d,20d),(72d,70d),(72.5d,75d),(double.NaN,70d)})
        {
            hoverDelay.SetValue(settings,input);Call(settings,"NormalizeInteraction");
            Assert((double)hoverDelay.GetValue(settings)! == expected,"hover delay normalization "+input);
        }
        var hoverSlider=(Slider)window.FindName("HoverDelaySlider");
        Assert(hoverSlider.Maximum==130 && hoverSlider.TickFrequency==5 && hoverSlider.IsSnapToTickEnabled && hoverSlider.SmallChange==5,"hover slider uses five millisecond steps");
        var badge=(FrameworkElement)window.FindName("UpdateBadge");
        typeof(SettingsWindow).GetField("_updateBadgeAvailable",Flags)!.SetValue(window,null);
        Call(window,"RefreshUpdateBadge",true);
        Assert(badge.Visibility==Visibility.Visible && badge.Opacity==1,"existing update badge shown directly on opening");
        Call(window,"RefreshUpdateBadge",false);Call(window,"RefreshUpdateBadge",true);
        var collapsed=settings.GetType().GetProperty("SettingsNavigationCollapsed")!;
        collapsed.SetValue(settings,true); Call(window,"ApplyNavigationLayout");
        Assert(((ColumnDefinition)window.FindName("NavigationColumn")).Width.Value==82,"collapsed width");
        foreach(var name in order) Assert(((StackPanel)((RadioButton)window.FindName(name+"Nav")).Content).Children[1].Visibility==Visibility.Hidden,"icon only "+name);
        collapsed.SetValue(settings,false); Call(window,"ApplyNavigationLayout");
        Assert(((ColumnDefinition)window.FindName("NavigationColumn")).Width.Value==200,"expanded width");
        var root = (FrameworkElement)window.Content;
        var progress = (System.Windows.Controls.ProgressBar)window.FindName("UpdateProgress");
        progress.Visibility = Visibility.Visible;
        progress.Value = 50;
        root.Measure(new Size(920,720)); root.Arrange(new Rect(0,0,920,720)); root.UpdateLayout();
        progress.ApplyTemplate();
        var track = (FrameworkElement)progress.Template.FindName("PART_Track",progress);
        var indicator = (Border)progress.Template.FindName("PART_Indicator",progress);
        Assert(indicator.CornerRadius.TopLeft==8 && progress.Height==16,"rounded progress matches slider track");
        Assert(Math.Abs(indicator.ActualWidth-track.ActualWidth/2)<1,"download progress fill reflects percentage");
        Assert(indicator.Child is null,"download progress has no thumb");
        Call(window,"TransitionUpdateProgress",75d,true);
        Assert(progress.HasAnimatedProperties,"download progress animates");
        Call(window,"TransitionUpdateProgress",75d,false);
        Call(window,"TransitionUpdateProgress",80d,true);
        var updateNav = (RadioButton)window.FindName("UpdateNav");
        var iconSurface = (FrameworkElement)((StackPanel)updateNav.Content).Children[0];
        var iconBounds = iconSurface.TransformToAncestor(updateNav).TransformBounds(new Rect(iconSurface.RenderSize));
        Assert(iconBounds.Top >= 0 && iconBounds.Bottom <= updateNav.ActualHeight, "GitHub icon fully inside row");
        var badgeBounds=badge.TransformToAncestor(iconSurface).TransformBounds(new Rect(badge.RenderSize));
        Assert(badgeBounds.Right<=iconSurface.ActualWidth && badgeBounds.Top>=0 && badgeBounds.Width==7,"badge fits icon upper right corner");
        foreach(var width in new[]{780d,920d})
        {
            root.Measure(new Size(width,720));root.Arrange(new Rect(0,0,width,720));root.UpdateLayout();
            var updateCard=(FrameworkElement)window.FindName("UpdateCard");
            var autoCheck=(FrameworkElement)window.FindName("AutoUpdateCheck");
            var checkButton=(FrameworkElement)window.FindName("CheckUpdateButton");
            var installButton=(FrameworkElement)window.FindName("InstallUpdateButton");
            Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(updateCard).TransformBounds(new Rect(element.RenderSize));
            Assert(Bounds(autoCheck).Right<=Bounds(checkButton).Left && Bounds(checkButton).Right<=Bounds(installButton).Left,"update buttons do not overlap switch at width "+width);
            Assert(Bounds(installButton).Right<=updateCard.ActualWidth && Math.Abs((Bounds(autoCheck).Top+Bounds(autoCheck).Bottom)/2-(Bounds(checkButton).Top+Bounds(checkButton).Bottom)/2)<6,"update actions stay on same row within card "+width);
            var openProject=(FrameworkElement)window.FindName("OpenProjectButton");
            var copyProject=(FrameworkElement)window.FindName("CopyProjectButton");
            Assert(Math.Abs(Bounds(openProject).Top-Bounds(copyProject).Top)<.01 && Math.Abs(Bounds(openProject).Bottom-Bounds(copyProject).Bottom)<.01,"project actions align horizontally at width "+width);
        }
        var brand = (StackPanel)window.FindName("NavigationBrand");
        var brandIcon = (FrameworkElement)brand.Children[0];
        var label = (FrameworkElement)((StackPanel)updateNav.Content).Children[1];
        var brandPosition = brandIcon.TranslatePoint(new Point(),root);
        var labelPosition = label.TranslatePoint(new Point(),root);
        collapsed.SetValue(settings,true); Call(window,"ApplyNavigationLayout");
        root.Measure(new Size(920,720)); root.Arrange(new Rect(0,0,920,720)); root.UpdateLayout();
        Assert(brandIcon.TranslatePoint(new Point(),root)==brandPosition,"brand icon position fixed");
        Assert(label.TranslatePoint(new Point(),root)==labelPosition,"label fade position fixed");
        collapsed.SetValue(settings,false); Call(window,"ApplyNavigationLayout");
        collapsed.SetValue(settings,true); Call(window,"TransitionNavigationLayout",true);
        Assert(window.HasAnimatedProperties,"navigation width transition active");
        collapsed.SetValue(settings,false); Call(window,"TransitionNavigationLayout",true);
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_,_) => { timer.Stop(); frame.Continue=false; }; timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Assert(((ColumnDefinition)window.FindName("NavigationColumn")).Width.Value==200,"rapid toggle settles expanded");
        Assert(badge.Visibility==Visibility.Visible && badge.Opacity==1,"rapid update badge reversal remains visible");
        Assert(progress.Visibility==Visibility.Visible && Math.Abs(progress.Value-80)<.01 && progress.Opacity==1,"rapid progress hide/show settles visible");
        foreach(var name in order) Assert(((StackPanel)((RadioButton)window.FindName(name+"Nav")).Content).Children[1].Visibility==Visibility.Visible,"rapid toggle label visible "+name);
        if(Environment.GetEnvironmentVariable("ISLAND_LAYOUT_EVIDENCE_DIR") is {Length:>0} evidence)
        {
            var notificationCard=(FrameworkElement)window.FindName("NotificationCard");
            notificationCard.BeginAnimation(UIElement.OpacityProperty,null);notificationCard.Opacity=1;
            ((Panel)notificationCard.Parent).Children.Remove(notificationCard);
            var evidenceWindow=new Window { Content=notificationCard,Width=620,SizeToContent=SizeToContent.Height,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None };
            evidenceWindow.Resources.MergedDictionaries.Add(window.Resources);evidenceWindow.Foreground=window.Foreground;
            evidenceWindow.Show();evidenceWindow.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(notificationCard.ActualWidth*1.5),(int)Math.Ceiling(notificationCard.ActualHeight*1.5),144,144,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(notificationCard);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            System.IO.Directory.CreateDirectory(evidence);
            using var file=System.IO.File.Create(System.IO.Path.Combine(evidence,"notification-settings.png"));encoder.Save(file);
            evidenceWindow.Close();
        }
        window.Close(); main.Close(); app.Shutdown();
    }
}

