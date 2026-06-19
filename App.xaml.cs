using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using HermesAgentTray.Models;
using HermesAgentTray.Services;
using Microsoft.Win32;

namespace HermesAgentTray;

public partial class App : Application
{
    private const string MutexName = "HermesAgentTray_SingleInstance";
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hermes", "hermes-starter.log");
    private System.Threading.Mutex? _mutex;
    private bool _mutexOwned;
    private TaskbarIcon? _notifyIcon;
    private HermesProcessManager? _processManager;
    private StatusWatcher? _statusWatcher;
    private HealthMonitor? _healthMonitor;
    private MainWindow? _mainWindow;
    private bool _showingMainWindow;
    public static AppSettings Settings { get; private set; } = new();
    private bool _isDarkTheme;

    public bool IsDarkTheme => _isDarkTheme;

    public static void LogError(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LogError("OnStartup called");

        // Single instance lock
        _mutex = new System.Threading.Mutex(true, MutexName, out _mutexOwned);
        if (!_mutexOwned)
        {
            MessageBox.Show("Hermes Agent Tray is already running.", "Hermes Agent Tray",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Catch unhandled exceptions
        DispatcherUnhandledException += (s, args) =>
        {
            LogError($"DispatcherUnhandledException: {args.Exception}");
            MessageBox.Show($"Unhandled error:\n\n{args.Exception.Message}\n\nSee log: {LogFile}",
                "Hermes Agent Tray", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            LogError("Initializing services...");

            // Load settings and apply language
            Settings = AppSettings.Load();
            Loc.Language = Settings.Language;

            // Apply theme
            ApplyTheme(Settings.Theme);

            _processManager = new HermesProcessManager();
            _statusWatcher = new StatusWatcher(_processManager);
            _healthMonitor = new HealthMonitor(_processManager);

            // Clean up any conflicting platform tokens inherited from default profile
            try { await ProfileManager.CleanupConflictingTokensAsync(); } catch (Exception ex) { LogError($"CleanupConflictingTokens failed: {ex.Message}"); }

            _statusWatcher.ProfileStatusChanged += (name, info) =>
                Dispatcher.Invoke(() => UpdateTrayToolTip());

            _processManager.ServiceStopped += (name, type) =>
            {
                // Just update tray tooltip, no error balloon
                Dispatcher.Invoke(() => UpdateTrayToolTip());
            };

            LogError("Creating tray icon in code...");
            CreateTrayIcon();
            LogError("Tray icon created");

            // Handle system shutdown/logoff — stop gateways gracefully
            SystemEvents.SessionEnding += (s, args) =>
            {
                LogError($"SessionEnding: reason={args.Reason}");
                try
                {
                    _processManager.StopAllSync();
                    LogError("All gateways stopped on session end");
                }
                catch (Exception ex)
                {
                    LogError($"StopAll on session end failed: {ex.Message}");
                }
            };

            LogError("Starting status watcher...");
            _statusWatcher.Start(5000);

            _healthMonitor!.GatewayHealthChanged += (profileName, isHealthy) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!isHealthy)
                    {
                        _notifyIcon?.ShowBalloonTip(
                            Loc.GatewayUnhealthyTitle,
                            Loc.GatewayUnhealthy(profileName),
                            BalloonIcon.Error);
                    }
                    UpdateTrayToolTip();
                });
            };
            _healthMonitor.Start(15000);

            LogError("Showing main window...");
            ShowMainWindow(true);
            LogError("Startup complete");

            // Auto-start gateways if enabled
            if (Settings.AutoStartGateways && Settings.AutoStartGatewayProfiles.Count > 0)
            {
                LogError($"Auto-starting {Settings.AutoStartGatewayProfiles.Count} gateway(s)...");
                foreach (var profile in Settings.AutoStartGatewayProfiles)
                {
                    try
                    {
                        if (!_processManager.IsRunning(profile, ServiceType.Gateway))
                        {
                            LogError($"Auto-starting gateway for profile: {profile}");
                            await _processManager.StartAsync(profile, ServiceType.Gateway);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Auto-start gateway failed for {profile}: {ex.Message}");
                    }
                }
            }

            // Auto-check for updates in background
            if (Settings.AutoCheckUpdate)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000); // Wait 5s after startup
                        var updateService = new UpdateService();
                        var release = await updateService.CheckForUpdateAsync();
                        if (release != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _notifyIcon?.ShowBalloonTip(
                                    Loc.UpdateTitle,
                                    Loc.UpdateAvailable(release.Version),
                                    BalloonIcon.Info);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Auto-update check failed: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LogError($"OnStartup error: {ex}");
            MessageBox.Show($"Failed to initialize:\n\n{ex.Message}\n\n{ex.GetType().Name}\n\nSee log: {LogFile}",
                "Hermes Agent Tray - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    public void ApplyTheme(string themeSetting)
    {
        var setting = themeSetting switch
        {
            "Dark" => AppTheme.Dark,
            "Light" => AppTheme.Light,
            _ => AppTheme.System
        };
        var effective = ThemeService.ResolveEffectiveTheme(setting);
        _isDarkTheme = effective == AppTheme.Dark;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(_isDarkTheme ? "Dark" : "Light")}.xaml")
        };

        // Remove old theme, add new
        var merged = Current.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source?.OriginalString?.Contains("/Themes/") == true)
                merged.RemoveAt(i);
        }
        merged.Add(dict);
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new TaskbarIcon();
        _notifyIcon.ToolTipText = "Hermes Agent Tray";
        _notifyIcon.Visibility = Visibility.Visible;

        // Try to load icon from embedded resource
        try
        {
            var iconUri = new Uri("pack://application:,,,/hermes.ico");
            _notifyIcon.IconSource = new BitmapImage(iconUri);
        }
        catch (Exception ex)
        {
            LogError($"Failed to load tray icon: {ex.Message}");
            // Use system default - TaskbarIcon can work without custom icon
        }

        _notifyIcon.TrayLeftMouseDown += (_, _) => ShowMainWindow();

        // Build context menu
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Hermes Agent Tray", IsEnabled = false });
        menu.Items.Add(new Separator());

        var startAll = new MenuItem { Header = "Start All Gateways" };
        startAll.Click += TrayMenu_StartAll;
        menu.Items.Add(startAll);

        var stopAll = new MenuItem { Header = "Stop All" };
        stopAll.Click += TrayMenu_StopAll;
        menu.Items.Add(stopAll);

        menu.Items.Add(new Separator());

        var openMgr = new MenuItem { Header = "Open Manager" };
        openMgr.Click += TrayMenu_ShowWindow;
        menu.Items.Add(openMgr);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += TrayMenu_Exit;
        menu.Items.Add(exit);

        _notifyIcon.ContextMenu = menu;
    }

    private void ShowMainWindow(bool startMinimized = false)
    {
        if (_showingMainWindow) return;
        _showingMainWindow = true;
        try
        {
            if (_mainWindow == null || !_mainWindow.IsLoaded)
            {
                _mainWindow = new MainWindow(_processManager!, _statusWatcher!, _healthMonitor!);
            }
            if (startMinimized)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.Show();
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }
        finally
        {
            _showingMainWindow = false;
        }
    }

    public void UpdateTrayToolTip()
    {
        if (_notifyIcon == null || _processManager == null) return;
        var profiles = ProfileManager.GetAllProfiles();
        var running = profiles.Count(p => _processManager.IsRunning(p.Name, ServiceType.Gateway));
        var unhealthy = profiles.Count(p => _healthMonitor?.IsHealthy(p.Name) == false);
        var tip = unhealthy > 0
            ? $"Hermes Agent Tray — {running}/{profiles.Count} running, {unhealthy} unhealthy!"
            : $"Hermes Agent Tray — {running}/{profiles.Count} running";
        _notifyIcon.ToolTipText = tip;
    }

    private void TrayMenu_StartAll(object sender, RoutedEventArgs e)
    {
        if (_processManager == null) return;
        var profiles = ProfileManager.GetAllProfiles();
        foreach (var p in profiles)
            _ = _processManager.StartAsync(p.Name, ServiceType.Gateway);
        UpdateTrayToolTip();
    }

    private async void TrayMenu_StopAll(object sender, RoutedEventArgs e)
    {
        if (_processManager == null) return;
        await _processManager.StopAllAsync();
        UpdateTrayToolTip();
    }

    private void TrayMenu_ShowWindow(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
    }

    private async void TrayMenu_Exit(object sender, RoutedEventArgs e)
    {
        if (_processManager != null)
            await _processManager.StopAllAsync();
        _notifyIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _healthMonitor?.Dispose();
        _statusWatcher?.Dispose();
        _notifyIcon?.Dispose();
        if (_mutexOwned && _mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
