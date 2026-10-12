using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
namespace YoyoClawCompanion;
public partial class SettingsWindow
{
    private void InitializeMagicSettings()
    {
        MagicEnableCheck.IsChecked=_island.CurrentSettings.EnableMagicAdaptation;
        MagicDurationSlider.Value=_island.CurrentSettings.MagicNotificationSeconds;
        MagicBlinkSlider.Value=_island.CurrentSettings.MagicBlinkPeriodMs;
        _island.MagicModeChanged+=MagicMode_Changed;
        Closed+=(_,_)=>_island.MagicModeChanged-=MagicMode_Changed;
        UpdateMagicControls(false);
    }
    private void MagicMode_Changed(object? sender,EventArgs e){UpdateDependencyStates();UpdateMagicControls(true);}
    private void MagicSetting_Changed(object sender,RoutedEventArgs e)
    {
        if(_loading||_closed)return;
        var current=_island.CurrentSettings;
        current.EnableMagicAdaptation=MagicEnableCheck.IsChecked==true;
        current.MagicNotificationSeconds=MagicDurationSlider.Value;
        current.MagicBlinkPeriodMs=(int)MagicBlinkSlider.Value;
        _island.ApplySettings(current);UpdateDependencyStates();UpdateMagicControls(true);
    }
    private void UpdateMagicControls(bool animate)
    {
        var installed=_island.MagicInstalled;var active=_island.MagicActive;
        if(MagicCard.Visibility!=(installed?Visibility.Visible:Visibility.Collapsed)){
            MagicCard.Visibility=installed?Visibility.Visible:Visibility.Collapsed;
            if(animate&&installed)MagicCard.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        }
        MagicNav.Visibility=MagicCard.Visibility;
        var loading=_loading;_loading=true;MagicEnableCheck.IsChecked=_island.CurrentSettings.EnableMagicAdaptation;_loading=loading;
        MagicStatusText.Text="当前状态："+_island.MagicStatus;
        MagicDurationLabel.Text=$"通知每轮显示时长 · {MagicDurationSlider.Value:0} 秒";
        MagicBlinkLabel.Text=$"提醒闪烁周期 · {MagicBlinkSlider.Value:0} 毫秒（越短越快）";
        MagicDurationSlider.IsEnabled=MagicBlinkSlider.IsEnabled=MagicEnableCheck.IsChecked==true;
        MagicNormalHint.Visibility=active?Visibility.Visible:Visibility.Collapsed;
        if(animate){MagicStatusText.BeginAnimation(OpacityProperty,new DoubleAnimation(.4,1,TimeSpan.FromMilliseconds(160)));if(active)MagicNormalHint.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));}
        foreach(var element in new FrameworkElement[]{PositionCard,CornerSlider,OpacitySlider,WidthSlider,ExpandedWidthSlider,HeightSlider,TextSizeSlider,ShadowCheck,TopmostCheck,ReadyClockCheck,HoverExpansionCheck,HoverDelayPanel,ReverseHoverCheck,SpringAnimationCheck,QuotaScrollSpeedSlider,ReplyFirstWhenExpandedUpCheck,AllowExpandedBeyondScreenCheck,MaxResponseLinesCombo,CompletionDisplayPanel,SystemNotificationDisplaySlider,FullscreenActiveOnlyCheck}){
            element.IsEnabled=!active;
            if (!active && ReferenceEquals(element,HoverDelayPanel)) element.IsEnabled=HoverExpansionCheck.IsChecked==true;
            if (!active && ReferenceEquals(element,CompletionDisplayPanel)) element.IsEnabled=CompletionNotificationsCheck.IsChecked==true||ConfirmationNotificationsCheck.IsChecked==true;
            element.ToolTip=active?"仅普通模式：退出 Magic 后恢复原有配置":null;
        }
    }
}
