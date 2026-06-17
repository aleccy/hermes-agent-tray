using System.Windows;
using System.Windows.Controls;
using HermesAgentTray.Services;

namespace HermesAgentTray;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly UpdateService _updateService = new();

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();

        _settings = settings;

        // Apply localization
        LblLanguage.Text = Loc.LanguageLabel;
        LblLanguageRestart.Text = Loc.LanguageRestart;
        LblTheme.Text = Loc.ThemeLabel;
        ChkAutoStart.Content = Loc.AutoStartLabel;
        ChkAutoStartGateways.Content = Loc.AutoStartGWLabel;
        LblAutoStartGwHint.Text = Loc.IsZh
            ? "勾选后，在主界面各 Profile 卡片上可勾选「自启」"
            : "Check this to enable per-profile auto-start checkboxes on the main screen";
        ChkAutoUpdate.Content = Loc.AutoUpdateLabel;
        BtnCheckUpdate.Content = Loc.CheckUpdateBtn;
        LblAbout.Text = Loc.AboutLabel;
        BtnCancel.Content = Loc.Cancel;
        BtnSave.Content = Loc.Save;

        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        LblVersion.Text = $"{Loc.VersionLabel}: {version}";
        LblAuthor.Text = Loc.IsZh ? "作者: alec_cy" : "Author: alec_cy";

        // Set current values
        CmbLanguage.SelectedIndex = _settings.Language == "zh-CN" ? 0 : 1;
        CmbTheme.SelectedIndex = _settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        ChkAutoStart.IsChecked = AppSettings.IsAutoStartEnabled();
        ChkAutoStartGateways.IsChecked = _settings.AutoStartGateways;
        ChkAutoUpdate.IsChecked = _settings.AutoCheckUpdate;

        // Apply dark title bar
        ThemeService.ApplyTitleBarDarkMode(this, ((App)Application.Current).IsDarkTheme);
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        LblUpdateStatus.Visibility = Visibility.Visible;
        LblUpdateStatus.Text = Loc.CheckingUpdate;

        try
        {
            var release = await _updateService.CheckForUpdateAsync();

            if (release == null)
            {
                LblUpdateStatus.Text = Loc.UpToDate;
                LblUpdateStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60));
                return;
            }

            var msg = Loc.UpdateAvailable(release.Version) +
                "\n\n" + (Loc.IsZh ? "是否立即下载并更新？" : "Download and install now?");

            var result = MessageBox.Show(this, msg, Loc.UpdateTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            LblUpdateStatus.Text = Loc.DownloadingUpdate;
            ProgressDownload.Visibility = Visibility.Visible;
            ProgressDownload.Value = 0;

            var progress = new Progress<(long downloaded, long total, string status)>(p =>
            {
                if (p.total > 0)
                {
                    var pct = (int)(p.downloaded * 100 / p.total);
                    ProgressDownload.Value = pct;
                    var sizeMB = p.downloaded / (1024.0 * 1024.0);
                    var totalMB = p.total / (1024.0 * 1024.0);
                    LblUpdateStatus.Text = $"{p.status} {pct}% ({sizeMB:F1}/{totalMB:F1} MB)";
                }
                else
                {
                    var sizeMB = p.downloaded / (1024.0 * 1024.0);
                    LblUpdateStatus.Text = $"{p.status} {sizeMB:F1} MB";
                }
            });

            var success = await _updateService.DownloadAndApplyUpdateAsync(release, this, progress);

            if (success)
            {
                ProgressDownload.Visibility = Visibility.Collapsed;
                var restart = MessageBox.Show(this, Loc.UpdateReadyRestart,
                    Loc.UpdateTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (restart == MessageBoxResult.Yes)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            }
            else
            {
                MessageBox.Show(this, Loc.DownloadFailed, Loc.Error,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                LblUpdateStatus.Text = Loc.DownloadFailed;
                ProgressDownload.Visibility = Visibility.Collapsed;
                LblUpdateStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
            }
        }
        catch
        {
            LblUpdateStatus.Text = Loc.UpdateCheckFailed;
            LblUpdateStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var selectedLang = ((ComboBoxItem)CmbLanguage.SelectedItem).Tag?.ToString() ?? "zh-CN";
        var selectedTheme = ((ComboBoxItem)CmbTheme.SelectedItem).Tag?.ToString() ?? "System";
        var autoStart = ChkAutoStart.IsChecked == true;
        var autoStartGw = ChkAutoStartGateways.IsChecked == true;
        var autoCheckUpdate = ChkAutoUpdate.IsChecked == true;

        _settings.Language = selectedLang;
        _settings.Theme = selectedTheme;
        _settings.AutoStart = autoStart;
        _settings.AutoStartGateways = autoStartGw;
        _settings.AutoCheckUpdate = autoCheckUpdate;
        _settings.Save();

        AppSettings.SetAutoStart(autoStart);

        // Apply theme immediately
        ((App)Application.Current).ApplyTheme(selectedTheme);

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
