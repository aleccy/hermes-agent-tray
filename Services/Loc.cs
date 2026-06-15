namespace HermesAgentTray.Services;

/// <summary>
/// Simple localization service supporting zh-CN and en-US.
/// </summary>
public static class Loc
{
    private static string _lang = "zh-CN";

    public static string Language
    {
        get => _lang;
        set => _lang = value;
    }

    public static bool IsZh => _lang == "zh-CN";

    // --- Common ---
    public static string AppTitle => IsZh ? "Hermes Agent 启动器" : "Hermes Agent Tray";
    public static string Ready => IsZh ? "就绪" : "Ready";
    public static string Config => IsZh ? "配置" : "Config";
    public static string Delete => IsZh ? "删除" : "Delete";
    public static string Console => IsZh ? "控制台" : "Console";
    public static string Start => IsZh ? "启动" : "Start";
    public static string Stop => IsZh ? "停止" : "Stop";
    public static string Open => IsZh ? "打开" : "Open";
    public static string Refresh => IsZh ? "刷新" : "Refresh";
    public static string Error => IsZh ? "错误" : "Error";
    public static string Cancel => IsZh ? "取消" : "Cancel";
    public static string Save => IsZh ? "保存" : "Save";

    // --- Toolbar ---
    public static string NewProfile => IsZh ? "+ 新建 Profile" : "+ New Profile";
    public static string StartAllGw => IsZh ? "全部启动 GW" : "Start All GW";
    public static string StopAll => IsZh ? "全部停止" : "Stop All";
    public static string Settings => IsZh ? "设置" : "Settings";

    // --- Profile Card ---
    public static string StartGW => IsZh ? "启动 GW" : "Start GW";
    public static string StopGW => IsZh ? "停止 GW" : "Stop GW";
    public static string Running => IsZh ? "运行中" : "Running";
    public static string Stopped => IsZh ? "已停止" : "Stopped";
    public static string Starting => IsZh ? "启动中..." : "Starting...";

    // --- Desktop & Dashboard ---
    public static string DesktopDashboard => IsZh ? "桌面 & 仪表板" : "Desktop & Dashboard";
    public static string DesktopLabel => IsZh ? "桌面" : "Desktop";
    public static string DashboardLabel => IsZh ? "仪表板" : "Dashboard";
    public static string WebUI => IsZh ? "Web 界面" : "Web UI";

    // --- Status Bar ---
    public static string GatewaysRunning(int running, int total) =>
        IsZh ? $"{running}/{total} 个网关运行中" : $"{running}/{total} gateway(s) running";

    // --- Settings Dialog ---
    public static string SettingsTitle => IsZh ? "设置" : "Settings";
    public static string LanguageLabel => IsZh ? "界面语言" : "Interface Language";
    public static string LanguageRestart => IsZh ? "（切换语言后需重启生效）" : "(Restart required to apply)";
    public static string AutoStartLabel => IsZh ? "开机自动启动" : "Auto-start on login";
    public static string AboutLabel => IsZh ? "关于" : "About";
    public static string VersionLabel => IsZh ? "版本" : "Version";
    public static string AutoStartGWLabel => IsZh ? "自动启动 Gateway" : "Auto-start Gateways";
    public static string AutoStartGW => IsZh ? "自启" : "Auto";

    // --- Profile Dialog ---
    public static string NewProfileTitle => IsZh ? "新建 Profile" : "New Profile";
    public static string EditProfileTitle(string name) =>
        IsZh ? $"编辑 Profile: {name}" : $"Edit Profile: {name}";
    public static string ProfileNameLabel => IsZh ? "Profile 名称" : "Profile Name";
    public static string ProfileNameHint => IsZh
        ? "API 密钥和平台 Token 从 default 继承。\n仅需配置用户差异项。"
        : "API keys and platform tokens are inherited from default.\nOnly configure user-specific settings below.";
    public static string ProfileNameLocked => IsZh ? "Profile 名称不可修改" : "Profile name cannot be changed";
    public static string EnvKey => IsZh ? "变量名" : "Key";
    public static string EnvValue => IsZh ? "值" : "Value";
    public static string AddVar => IsZh ? "+ 添加变量" : "+ Add Variable";
    public static string LoadEnv => IsZh ? "导入 .env" : "Load .env";
}
