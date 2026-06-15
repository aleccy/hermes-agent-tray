using System.Windows;
using System.Windows.Controls;
using HermesAgentTray.Services;

namespace HermesAgentTray;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();

        _settings = settings;

        // Apply localization
        LblLanguage.Text = Loc.LanguageLabel;
        LblLanguageRestart.Text = Loc.LanguageRestart;
        ChkAutoStart.Content = Loc.AutoStartLabel;
        ChkAutoStartGateways.Content = Loc.AutoStartGWLabel;
        LblAutoStartGwHint.Text = Loc.IsZh
            ? "勾选后，在主界面各 Profile 卡片上可勾选「自启」"
            : "Check this to enable per-profile auto-start checkboxes on the main screen";
        LblAbout.Text = Loc.AboutLabel;
        BtnCancel.Content = Loc.Cancel;
        BtnSave.Content = Loc.Save;

        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        LblVersion.Text = $"{Loc.VersionLabel}: {version}";
        LblAuthor.Text = Loc.IsZh ? "作者: alec_cy" : "Author: alec_cy";

        // Set current values
        CmbLanguage.SelectedIndex = _settings.Language == "zh-CN" ? 0 : 1;
        ChkAutoStart.IsChecked = AppSettings.IsAutoStartEnabled();
        ChkAutoStartGateways.IsChecked = _settings.AutoStartGateways;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var selectedLang = ((ComboBoxItem)CmbLanguage.SelectedItem).Tag?.ToString() ?? "zh-CN";
        var autoStart = ChkAutoStart.IsChecked == true;
        var autoStartGw = ChkAutoStartGateways.IsChecked == true;

        _settings.Language = selectedLang;
        _settings.AutoStart = autoStart;
        _settings.AutoStartGateways = autoStartGw;
        _settings.Save();

        AppSettings.SetAutoStart(autoStart);

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
