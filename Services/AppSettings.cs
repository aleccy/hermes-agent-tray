using System.IO;
using System.Text.Json;

namespace HermesAgentTray.Services;

public class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public bool AutoStart { get; set; } = false;
    public bool AutoStartGateways { get; set; } = false;
    public bool AutoCheckUpdate { get; set; } = true;
    public string Theme { get; set; } = "System";

    /// <summary>
    /// List of profile names whose gateways should auto-start when the app launches.
    /// </summary>
    public List<string> AutoStartGatewayProfiles { get; set; } = new();

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HermesAgentTray", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    public bool IsGatewayAutoStart(string profileName)
        => AutoStartGateways && AutoStartGatewayProfiles.Contains(profileName);

    public void SetGatewayAutoStart(string profileName, bool enable)
    {
        if (enable)
        {
            if (!AutoStartGatewayProfiles.Contains(profileName))
                AutoStartGatewayProfiles.Add(profileName);
        }
        else
        {
            AutoStartGatewayProfiles.Remove(profileName);
        }
    }

    /// <summary>
    /// Enable or disable Windows auto-start via registry.
    /// </summary>
    public static void SetAutoStart(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "HermesAgentTray";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                    key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                if (key.GetValue(appName) != null)
                    key.DeleteValue(appName);
            }
        }
        catch { }
    }

    public static bool IsAutoStartEnabled()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "HermesAgentTray";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, false);
            return key?.GetValue(appName) != null;
        }
        catch { return false; }
    }
}
