using System.Reflection;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using YoyoClawCompanion;
class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Call(object o,string method,params object[] args) => o.GetType().GetMethod(method,Flags)!.Invoke(o,args);
    static void Assert(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); }
    [STAThread] static void Main()
    {
        var app=new App { ShutdownMode=ShutdownMode.OnExplicitShutdown }; typeof(App).GetMethod("InitializeComponent",Flags)?.Invoke(app,null);
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
        var reverse=settings.GetType().GetProperty("EnableReverseHover")!;
        var hover=settings.GetType().GetProperty("EnableHoverExpansion")!;
        reverse.SetValue(settings,true); hover.SetValue(settings,true); Call(settings,"NormalizeInteraction");
        Assert(!(bool)hover.GetValue(settings)!,"legacy conflicting modes normalize");
        for(int mask=0;mask<8;mask++) {
            string[] keys={"yoyo","codex","workbuddy"}; string[] fields={"_yoyoInstalled","_codexInstalled","_workBuddyInstalled"};
            for(int i=0;i<3;i++) typeof(MainWindow).GetField(fields[i],Flags)!.SetValue(main,(mask&(1<<i))!=0);
            Call(window,"UpdateDependencyStates"); Call(window,"LoadProviderOrder","yoyo,codex,workbuddy");
            var list=((IEnumerable)window.GetType().GetProperty("ProviderOrderItems")!.GetValue(window)!).Cast<object>().Select(x=>(string)x.GetType().GetProperty("Key")!.GetValue(x)!).ToArray();
            Assert(list.SequenceEqual(keys.Where((_,i)=>(mask&(1<<i))!=0)),"installed ordering mask "+mask);
            foreach(var (name,bit) in new[]{("YoyoCheck",0),("YoyoCreditsCheck",0),("YoyoAutoCheckinCheck",0),("CodexCheck",1),("CodexActivityCheck",1),("CodexLimitsCheck",1),("CodexResetReminderCheck",1),("WorkBuddyCheck",2),("WorkBuddyCreditsCheck",2),("ConfirmationNotificationsCheck",2)})
                if(((UIElement)window.FindName(name)).IsEnabled != ((mask&(1<<bit))!=0)) throw new Exception("enabled state "+name+" mask "+mask);
        }
        var collapsed=settings.GetType().GetProperty("SettingsNavigationCollapsed")!;
        collapsed.SetValue(settings,true); Call(window,"ApplyNavigationLayout");
        Assert(((ColumnDefinition)window.FindName("NavigationColumn")).Width.Value==82,"collapsed width");
        foreach(var name in order) Assert(((StackPanel)((RadioButton)window.FindName(name+"Nav")).Content).Children[1].Visibility==Visibility.Collapsed,"icon only "+name);
        collapsed.SetValue(settings,false); Call(window,"ApplyNavigationLayout");
        Assert(((ColumnDefinition)window.FindName("NavigationColumn")).Width.Value==200,"expanded width");
        window.Close(); main.Close(); app.Shutdown();
    }
}

