using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using YoyoClawCompanion.Controls;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class SettingsWindow
{
    private SettingsSwitchPanel[] _settingPanels = [];
    private readonly List<SettingCardHint> _settingHints = [];
    private bool _editingSettingCards;
    private int _editBarVersion;

    private void InitializeSettingCardEditing()
    {
        _settingPanels = new[] { ReplyFirstWhenExpandedUpCheckSettingsPanel, ShadowCheckSettingsPanel,
            YoyoCheckSettingsPanel, CodexActivityCheckSettingsPanel, CompletionNotificationsCheckSettingsPanel, AutoUpdateCheckSettingsPanel };
        foreach (var panel in _settingPanels)
        {
            panel.EditRequested += (_,_) => SetSettingCardEditing(true);
            panel.OrderChanged += (_,_) =>
            {
                var current = _island.CurrentSettings;
                current.SettingCardOrders ??= new();
                current.SettingCardOrders[panel.Name] = panel.Order.ToArray();
                AppSettings.Save(current);
            };
            foreach (FrameworkElement item in panel.Children)
                _settingHints.Add(new SettingCardHint(item is ExpandableSettingCard card ? card.Header : item));
        }
        PreviewKeyDown += (_,e) =>
        {
            if (!_editingSettingCards) return;
            if (e.Key == Key.Escape) { SetSettingCardEditing(false); e.Handled = true; }
            else if (e.Key is Key.Space or Key.Enter && e.OriginalSource is System.Windows.Controls.CheckBox) e.Handled = true;
        };
    }

    private void LoadSettingCardOrders(IslandSettings settings)
    {
        foreach (var panel in _settingPanels)
            if (settings.SettingCardOrders?.TryGetValue(panel.Name,out var order) == true && order is not null)
                panel.ApplyOrder(order);
    }

    private void SetSettingCardEditing(bool editing)
    {
        if (_editingSettingCards == editing) return;
        _editingSettingCards = editing;
        foreach (var hint in _settingHints) hint.SetEnabled(!editing);
        foreach (var panel in _settingPanels) panel.SetEditing(editing);
        var version = ++_editBarVersion;
        var from = SettingEditBar.Visibility == Visibility.Visible ? SettingEditBar.Opacity : 0;
        SettingEditBar.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(from,editing ? 1 : 0,TimeSpan.FromMilliseconds(180));
        fade.Completed += (_,_) => { if (version == _editBarVersion && !editing) SettingEditBar.Visibility = Visibility.Collapsed; };
        SettingEditBar.BeginAnimation(OpacityProperty,fade);
    }

    private void FinishSettingEdit_Click(object sender,RoutedEventArgs e) => SetSettingCardEditing(false);
}
