using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;
public partial class MainWindow
{
    private object? _magicAdapter;
    private Action<string>? _magicConfigure,_magicUpdate;
    private Action<string,string,string,string>? _magicNotice;
    private Action<string?,string?>? _magicQuestion;
    private Action? _magicStop;
    private bool _magicInstalled,_magicDetectionDone;
    private string? _magicError;
    private nint _magicFailedHandle;
    private string _magicLastStatus="";
    internal bool MagicActive=>_magicAdapter!=null;
    internal bool MagicInstalled=>_magicInstalled;
    internal string MagicStatus=>_magicError??(!_magicDetectionDone?"正在识别":!_magicInstalled?"未安装":MagicActive?"Magic 模式":MagicBarHandle()==0?"未运行":"普通模式 · 适配已关闭");
    internal event EventHandler? MagicModeChanged;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint FindWindow(string? type,string title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle,out MagicRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct MagicRect {public int Left,Top,Right,Bottom;}
    private static nint MagicBarHandle(){var handle=FindWindow(null,"Magictopbar");return handle!=0&&GetWindowRect(handle,out var bounds)&&bounds.Right-bounds.Left>600?handle:0;}
    private async Task InitializeMagicAsync()
    {
        _magicInstalled=await Task.Run(()=>File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"HONOR","MagicAnimation","MagicTaskbarApp","magictaskbar-ui.exe"))||MagicBarHandle()!=0);
        if(_lifetimeCancellation.IsCancellationRequested)return;
        _magicDetectionDone=true;
        if(Environment.GetCommandLineArgs().Contains("--magic-preview"))_settings.EnableMagicAdaptation=true;
        CheckMagicMode();MagicModeChanged?.Invoke(this,EventArgs.Empty);
    }
    private T MagicMethod<T>(string name) where T:Delegate=>_magicAdapter!.GetType().GetMethod(name)!.CreateDelegate<T>(_magicAdapter);
    private void CheckMagicMode()
    {
        if(!_magicDetectionDone||_lifetimeCancellation.IsCancellationRequested)return;
        var bar=MagicBarHandle();
        var desired=_magicInstalled&&_settings.EnableMagicAdaptation&&bar!=0;
        if(desired&&bar==_magicFailedHandle)return;
        if(!desired){_magicFailedHandle=0;_magicError=null;}
        if(desired==MagicActive){var status=MagicStatus;if(status!=_magicLastStatus){_magicLastStatus=status;MagicModeChanged?.Invoke(this,EventArgs.Empty);}return;}
        if(desired)
        {
            try {
                // No static type reference: users without Magic never load this assembly/native DLL.
                var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MagicStatusbarAdapter.dll"));
                Action<string> launch=name=>{if(_settings.EnableAppLaunch)OpenProviderFromMenu(name switch{"Codex"=>"codex","WorkBuddy"=>"workbuddy","YOYO" or "YOYO Claw"=>"yoyo","DeepSeek Harness"=>"deepseek",_=>name});};
                Action disable=()=>{_settings.EnableMagicAdaptation=false;PersistSettingsAsync();CheckMagicMode();};
                _magicAdapter=Activator.CreateInstance(assembly.GetType("MagicStatusbarAdapter.MagicAdapter")!,launch,(Action)OpenHome,disable);
                _magicConfigure=MagicMethod<Action<string>>("Configure");_magicUpdate=MagicMethod<Action<string>>("Update");
                _magicNotice=MagicMethod<Action<string,string,string,string>>("Notify");_magicQuestion=MagicMethod<Action<string?,string?>>("Question");_magicStop=MagicMethod<Action>("Stop");
                _magicConfigure(JsonSerializer.Serialize(_settings));StatusSnapshotStore.Published+=PublishMagicSnapshot;
                _magicError=null;HideIslandForFocusMode(animate:true);
                _readyClockTimer.Stop();_passThroughTimer.Stop();_fullscreenTimer.Stop();
                _ = RefreshStatusAsync();
            } catch(Exception exception){_magicError="适配不可用 · "+exception.GetBaseException().Message;StopMagic();_magicFailedHandle=bar;UpdateDisplayMode();}
        }
        else {StopMagic();_fullscreenOverrideActive=false;UpdateDisplayMode();if(_settings.EnableFullscreenActiveOnly)_fullscreenTimer.Start();}
        _magicLastStatus=MagicStatus;MagicModeChanged?.Invoke(this,EventArgs.Empty);
    }
    private void PublishMagicSnapshot(string json)=>_magicUpdate?.Invoke(json);
    private void StopMagic()
    {
        StatusSnapshotStore.Published-=PublishMagicSnapshot;
        try{_magicStop?.Invoke();}finally{_magicAdapter=null;_magicConfigure=null;_magicUpdate=null;_magicNotice=null;_magicQuestion=null;_magicStop=null;}
    }
    private void UpdateMagicSettings(){if(!_magicDetectionDone)return;CheckMagicMode();_magicConfigure?.Invoke(JsonSerializer.Serialize(_settings));}
}
