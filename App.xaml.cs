using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using HermesAgentTray.Models;
using HermesAgentTray.Services;

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
    private MainWindow? _mainWindow;
    public static AppSettings Settings { get; private set; } = new();

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

            _processManager = new HermesProcessManager();
            _statusWatcher = new StatusWatcher(_processManager);

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

            LogError("Starting status watcher...");
            _statusWatcher.Start(5000);

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
        }
        catch (Exception ex)
        {
            LogError($"OnStartup error: {ex}");
            MessageBox.Show($"Failed to initialize:\n\n{ex.Message}\n\n{ex.GetType().Name}\n\nSee log: {LogFile}",
                "Hermes Agent Tray - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
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
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow(_processManager!, _statusWatcher!);
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

    private void UpdateTrayToolTip()
    {
        if (_notifyIcon == null || _processManager == null) return;
        var profiles = ProfileManager.GetAllProfiles();
        var running = profiles.Count(p => _processManager.IsRunning(p.Name, ServiceType.Gateway));
        _notifyIcon.ToolTipText = $"Hermes Agent Tray — {running}/{profiles.Count} running";
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
