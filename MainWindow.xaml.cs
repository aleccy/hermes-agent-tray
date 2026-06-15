using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HermesAgentTray.Models;
using HermesAgentTray.Services;
using HermesAgentTray.ViewModels;

namespace HermesAgentTray;

public partial class MainWindow : Window
{
    private readonly HermesProcessManager _processManager;
    private readonly StatusWatcher _statusWatcher;

    // Track which profile the Desktop/Dashboard was started with
    private string _desktopProfile = "default";
    private string _dashboardProfile = "default";

    public MainWindow(HermesProcessManager processManager, StatusWatcher statusWatcher)
    {
        InitializeComponent();

        _processManager = processManager;
        _statusWatcher = statusWatcher;

        _statusWatcher.ProfileStatusChanged += OnProfileStatusChanged;
        _processManager.ServiceStopped += OnServiceStopped;
        _processManager.ServiceStarted += OnServiceStarted;

        ApplyLocalization();
        RefreshProfileList();
    }

    private void ApplyLocalization()
    {
        Title = Loc.AppTitle;
        TitleText.Text = Loc.AppTitle;
        BtnNewProfile.Content = Loc.NewProfile;
        BtnStartAll.Content = Loc.StartAllGw;
        BtnStopAll.Content = Loc.StopAll;
        BtnRefresh.Content = Loc.Refresh;
        BtnSettings.Content = Loc.Settings;
        DesktopDashboardTitle.Text = Loc.DesktopDashboard;
    }

    private void RefreshProfileList()
    {
        ProfilePanel.Children.Clear();
        var profiles = ProfileManager.GetAllProfiles();
        var viewModels = profiles.Select(p => new ProfileViewModel(p, _processManager)).ToList();

        foreach (var vm in viewModels)
        {
            var card = CreateProfileCard(vm);
            ProfilePanel.Children.Add(card);
        }

        UpdateDesktopDashboardPanel();
        UpdateStatusBar(viewModels);
    }

    private Border CreateProfileCard(ProfileViewModel vm)
    {
        var name = vm.Name;

        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            BorderBrush = vm.GatewayRunning
                ? new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60))
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        };

        var dock = new DockPanel();

        // Left: status dot + profile name + gateway status
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        leftPanel.Children.Add(new Ellipse
        {
            Width = 10, Height = 10,
            Fill = vm.GatewayRunning ? new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)) :
                                     new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7)),
            Margin = new Thickness(0, 0, 8, 0),
        });
        leftPanel.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
            Margin = new Thickness(0, 0, 12, 0),
        });
        leftPanel.Children.Add(new TextBlock
        {
            Text = vm.GatewayStatusText,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        DockPanel.SetDock(leftPanel, Dock.Left);
        dock.Children.Add(leftPanel);

        // Right: AutoStart checkbox + Start/Stop + Config + Delete buttons
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var autoStartCb = new CheckBox
        {
            Content = Loc.AutoStartGW,
            IsChecked = App.Settings.IsGatewayAutoStart(name),
            IsEnabled = App.Settings.AutoStartGateways,
            FontSize = 11,
            Foreground = App.Settings.AutoStartGateways
                ? new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7)),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = App.Settings.AutoStartGateways
                ? (Loc.IsZh ? "启动应用时自动启动此 Gateway" : "Auto-start this gateway when app launches")
                : (Loc.IsZh ? "请先在设置中开启「自动启动 Gateway」" : "Enable 'Auto-start Gateways' in Settings first"),
        };
        autoStartCb.Click += (_, _) =>
        {
            App.Settings.SetGatewayAutoStart(name, autoStartCb.IsChecked == true);
            App.Settings.Save();
        };
        rightPanel.Children.Add(autoStartCb);

        rightPanel.Children.Add(MakeBtn(
            vm.GatewayRunning ? Loc.StopGW : Loc.StartGW,
            vm.GatewayRunning ? "#E74C3C" : "#27AE60",
            () => ToggleGateway(name), padding: "8,3"));

        rightPanel.Children.Add(MakeBtn(Loc.Console, "#2C3E50", () => OpenConsole(name), padding: "6,3"));
        rightPanel.Children.Add(MakeBtn(Loc.Config, "#8E44AD", () => EditProfile(name), padding: "6,3"));
        if (vm.CanDelete)
            rightPanel.Children.Add(MakeBtn(Loc.Delete, "#E74C3C", () => DeleteProfile(name), padding: "6,3"));

        DockPanel.SetDock(rightPanel, Dock.Right);
        dock.Children.Add(rightPanel);

        card.Child = dock;
        return card;
    }

    /// <summary>
    /// Build or rebuild the Desktop/Dashboard panel at the bottom.
    /// Desktop/Dashboard are global singletons — they have their own profile switcher inside.
    /// HermesAgentTray just starts/stops them and provides the Open button.
    /// </summary>
    private void UpdateDesktopDashboardPanel()
    {
        DesktopDashboardPanel.Children.Clear();

        var desktopRunningProfile = _processManager.GetRunningProfile(ServiceType.Desktop);
        var desktopRunning = desktopRunningProfile != null;
        var desktopStarting = _processManager.IsStartingAny(ServiceType.Desktop);
        if (desktopRunning && desktopRunningProfile != null)
            _desktopProfile = desktopRunningProfile;
        var dashboardRunning = _processManager.IsDashboardRunning();
        var dashboardStarting = _processManager.IsStartingAny(ServiceType.Dashboard);
        var dashboardPort = _processManager.GetDashboardPort(_dashboardProfile);
        var dashboardRunningProfile = _processManager.GetDashboardRunningProfile();
        if (dashboardRunning && dashboardRunningProfile != null)
            _dashboardProfile = dashboardRunningProfile;

        // Desktop section
        var desktopDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        var desktopLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        desktopLeft.Children.Add(new Ellipse
        {
            Width = 10, Height = 10,
            Fill = desktopRunning ? new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)) :
                  desktopStarting ? new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)) :
                                    new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7)),
            Margin = new Thickness(0, 0, 8, 0),
        });
        desktopLeft.Children.Add(new TextBlock
        {
            Text = Loc.DesktopLabel,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
            Margin = new Thickness(0, 0, 12, 0),
        });
        desktopLeft.Children.Add(new TextBlock
        {
            Text = desktopRunning ? Loc.Running : desktopStarting ? Loc.Starting : Loc.Stopped,
            FontSize = 11,
            Foreground = desktopStarting ? new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)) :
                        new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        DockPanel.SetDock(desktopLeft, Dock.Left);
        desktopDock.Children.Add(desktopLeft);

        var desktopRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var desktopBtn = MakeBtn(
            desktopRunning ? Loc.Stop : Loc.Start,
            desktopRunning ? "#E74C3C" : "#27AE60",
            () => ToggleDesktop(_desktopProfile), padding: "8,3");
        if (desktopStarting)
        {
            desktopBtn.IsEnabled = false;
            desktopBtn.Background = new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7));
        }
        desktopRight.Children.Add(desktopBtn);

        DockPanel.SetDock(desktopRight, Dock.Right);
        desktopDock.Children.Add(desktopRight);

        DesktopDashboardPanel.Children.Add(desktopDock);

        // Dashboard section
        var dashDock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };

        var dashLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dashLeft.Children.Add(new Ellipse
        {
            Width = 10, Height = 10,
            Fill = dashboardRunning ? new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)) :
                   dashboardStarting ? new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)) :
                                     new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7)),
            Margin = new Thickness(0, 0, 8, 0),
        });
        dashLeft.Children.Add(new TextBlock
        {
            Text = Loc.DashboardLabel,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
            Margin = new Thickness(0, 0, 12, 0),
        });
        dashLeft.Children.Add(new TextBlock
        {
            Text = dashboardRunning ? $"http://127.0.0.1:{dashboardPort}" :
                  dashboardStarting ? Loc.Starting : $"{Loc.Stopped} ({Loc.WebUI})",
            FontSize = 11,
            Foreground = dashboardStarting ? new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)) :
                        new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        DockPanel.SetDock(dashLeft, Dock.Left);
        dashDock.Children.Add(dashLeft);

        var dashRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dashBtn = MakeBtn(
            dashboardRunning ? Loc.Stop : Loc.Start,
            dashboardRunning ? "#E74C3C" : "#27AE60",
            () => ToggleDashboard(_dashboardProfile), padding: "8,3");
        if (dashboardStarting)
        {
            dashBtn.IsEnabled = false;
            dashBtn.Background = new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7));
        }
        dashRight.Children.Add(dashBtn);

        if (dashboardRunning)
            dashRight.Children.Add(MakeBtn(Loc.Open, "#16A085", () => OpenDashboard(_dashboardProfile), padding: "8,3"));

        DockPanel.SetDock(dashRight, Dock.Right);
        dashDock.Children.Add(dashRight);

        DesktopDashboardPanel.Children.Add(dashDock);
    }

    private static Button MakeBtn(string text, string bgColor, Action onClick, string padding = "8,3", int fontSize = 11)
    {
        var color = (Color)ColorConverter.ConvertFromString(bgColor);
        var parts = padding.Split(',');
        var pad = parts.Length == 2
            ? new Thickness(double.Parse(parts[0]), double.Parse(parts[1]), double.Parse(parts[0]), double.Parse(parts[1]))
            : new Thickness(8, 3, 8, 3);

        var btn = new Button
        {
            Content = text,
            Padding = pad,
            Margin = new Thickness(2, 0, 2, 0),
            FontSize = fontSize,
            Background = new SolidColorBrush(color),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void OnProfileStatusChanged(string name, ProfileInfo info)
    {
        Dispatcher.Invoke(() => RefreshProfileList());
    }

    private void OnServiceStopped(string profileName, ServiceType type)
    {
        Dispatcher.Invoke(() => RefreshProfileList());
    }

    private void OnServiceStarted(string profileName, ServiceType type)
    {
        // Refresh UI to show "Starting..." state, then schedule another refresh
        // after a few seconds to pick up the "Running" state
        Dispatcher.Invoke(() => RefreshProfileList());

        // Schedule delayed refresh to transition from "Starting" to "Running"
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            Dispatcher.Invoke(() => RefreshProfileList());
        });
    }

    private void UpdateStatusBar(List<ProfileViewModel> profiles)
    {
        var running = profiles.Count(p => p.GatewayRunning);
        StatusBar.Text = Loc.GatewaysRunning(running, profiles.Count);
    }

    // --- Toolbar Buttons ---

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(App.Settings);
        if (dialog.ShowDialog() == true)
        {
            Loc.Language = App.Settings.Language;
            ApplyLocalization();
            RefreshProfileList();
        }
    }

    private async void BtnAddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileDialog();
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await ProfileManager.CreateProfileAsync(dialog.ProfileName, dialog.EnvValues);
                RefreshProfileList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnStartAll_Click(object sender, RoutedEventArgs e)
    {
        var profiles = ProfileManager.GetAllProfiles();
        foreach (var p in profiles)
            await _processManager.StartAsync(p.Name, ServiceType.Gateway);
        RefreshProfileList();
    }

    private async void BtnStopAll_Click(object sender, RoutedEventArgs e)
    {
        await _processManager.StopAllAsync();
        RefreshProfileList();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshProfileList();
    }

    // --- Per-Profile Actions ---

    private async void ToggleGateway(string name)
    {
        try
        {
            if (_processManager.IsRunning(name, ServiceType.Gateway))
                await _processManager.StopAsync(name, ServiceType.Gateway);
            else
                await _processManager.StartAsync(name, ServiceType.Gateway);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Gateway Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        RefreshProfileList();
    }

    private async void ToggleDesktop(string profileName)
    {
        try
        {
            // Find running desktop regardless of profile
            var runningDesktopProfile = _processManager.GetRunningProfile(ServiceType.Desktop);
            if (runningDesktopProfile != null)
            {
                await _processManager.StopAsync(runningDesktopProfile, ServiceType.Desktop);
            }
            else
            {
                await _processManager.StartAsync(profileName, ServiceType.Desktop);
                _desktopProfile = profileName;
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Desktop Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        RefreshProfileList();
    }

    private async void ToggleDashboard(string profileName)
    {
        try
        {
            if (_processManager.IsDashboardRunning())
            {
                var runningProfile = _processManager.GetDashboardRunningProfile();
                App.LogError($"ToggleDashboard: stopping, runningProfile={runningProfile}");
                if (runningProfile != null)
                    await _processManager.StopAsync(runningProfile, ServiceType.Dashboard);
            }
            else
            {
                App.LogError($"ToggleDashboard: starting with profileName={profileName}");
                await _processManager.StartAsync(profileName, ServiceType.Dashboard);
                _dashboardProfile = profileName;
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Dashboard Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        RefreshProfileList();
    }

    private void OpenDashboard(string profileName)
    {
        var port = _processManager.GetDashboardPort(profileName);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://127.0.0.1:{port}") { UseShellExecute = true }); } catch { }
    }

    private void OpenConsole(string name)
    {
        var profileHome = ProfileManager.GetProfileHome(name);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"set HERMES_HOME={profileHome} && echo Hermes Console: {name} && echo HERMES_HOME={profileHome} && echo.\"",
                UseShellExecute = false,
                WorkingDirectory = profileHome,
            };
            // Load .env variables into the console
            var envFile = System.IO.Path.Combine(profileHome, ".env");
            if (System.IO.File.Exists(envFile))
            {
                foreach (var line in System.IO.File.ReadAllLines(envFile))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var k = trimmed[..eqIdx].Trim();
                        var v = trimmed[(eqIdx + 1)..].Trim().Trim('"');
                        psi.EnvironmentVariables[k] = v;
                    }
                }
            }
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditProfile(string name)
    {
        var profile = ProfileManager.GetProfile(name);
        if (profile == null) return;

        var env = ProfileManager.LoadEnv(name);
        var dialog = new ProfileDialog(name, env);
        if (dialog.ShowDialog() == true)
        {
            foreach (var kv in dialog.EnvValues)
                ProfileManager.SaveEnvValueAsync(name, kv.Key, kv.Value).Wait();
            RefreshProfileList();
        }
    }

    private async void DeleteProfile(string name)
    {
        if (name == "default") return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete profile '{name}'?\nThis cannot be undone.",
            "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _processManager.StopAllForProfileAsync(name);
            try
            {
                ProfileManager.DeleteProfile(name);
                RefreshProfileList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // --- Minimize to tray ---

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }
}
