using System.Text.Json;
namespace MagicStatusbarAdapter;

// No collectors, file watchers, tray or helper processes: the host owns all data.
public sealed class MagicAdapter
{
    private readonly StatusForm form;
    public MagicAdapter(Action<string> launch,Action settings,Action disable){form=new(launch,settings,disable);form.Show();}
    public void Configure(string json)=>form.Configure(json);
    public void Update(string json)=>form.Update(json);
    public void Notify(string source,string title,string body,string key)=>form.Notify(source,title,body,key);
    public void Question(string? id,string? prompt)=>form.Question(id,prompt);
    public void Stop()=>form.Stop();
}
internal sealed class AdapterOptions
{
    public double MagicNotificationSeconds {get;set;}=8;
    public int MagicBlinkPeriodMs {get;set;}=700;
    public bool ShowYoyo {get;set;}=true;
    public bool ShowCodex {get;set;}=true;
    public bool ShowWorkBuddy {get;set;}=true;
    public bool ShowDeepSeek {get;set;}=true;
    public bool ShowYoyoCredits {get;set;}=true;
    public bool ShowCodexLimits {get;set;}=true;
    public bool ShowWorkBuddyCredits {get;set;}=true;
    public bool EnableAppLaunch {get;set;}=true;
    public bool EnableCodexConfirmationNotifications {get;set;}=true;
    public bool EnableConfirmationNotifications {get;set;}=true;
    public bool EnableUnchangedAutoHide {get;set;}
    public double UnchangedAutoHideMinutes {get;set;}=5;
    public string DisplayMode {get;set;}="always";
    public string ProviderOrder {get;set;}="yoyo,codex,workbuddy,deepseek";
    internal DateTimeOffset LastChangeAt=DateTimeOffset.Now;
    internal string Fingerprint="";
    internal bool ActiveOnly=>DisplayMode=="activeOnly";
    internal static string Key(string name)=>name switch{"Codex"=>"codex","WorkBuddy"=>"workbuddy","YOYO" or "YOYO Claw"=>"yoyo","DeepSeek Harness"=>"deepseek",_=>name};
    internal bool Visible(string name)=>Key(name) switch{"codex"=>ShowCodex,"workbuddy"=>ShowWorkBuddy,"yoyo"=>ShowYoyo,"deepseek"=>ShowDeepSeek,_=>false};
    internal bool Credits(string name)=>Key(name) switch{"codex"=>ShowCodexLimits,"workbuddy"=>ShowWorkBuddyCredits,"yoyo"=>ShowYoyoCredits,_=>true};
    internal int Order(string name){var index=Array.IndexOf(ProviderOrder.Split(','),Key(name));return index<0?99:index;}
}
