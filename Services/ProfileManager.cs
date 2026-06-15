using System.IO;
using HermesAgentTray.Models;

namespace HermesAgentTray.Services;

public static class ProfileManager
{
    private static readonly string HermesHome = Environment.GetEnvironmentVariable("HERMES_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hermes");

    public static string GetProfileHome(string profileName)
    {
        if (profileName == "default")
            return HermesHome;
        return Path.Combine(HermesHome, "profiles", profileName);
    }

    public static List<ProfileInfo> GetAllProfiles()
    {
        var profiles = new List<ProfileInfo>();

        if (Directory.Exists(HermesHome))
        {
            profiles.Add(new ProfileInfo
            {
                Name = "default",
                Path = HermesHome,
                IsDefault = true,
                HasEnv = File.Exists(Path.Combine(HermesHome, ".env")),
                HasConfig = File.Exists(Path.Combine(HermesHome, "config.yaml")),
                HasSoul = File.Exists(Path.Combine(HermesHome, "SOUL.md")),
            });
        }

        var profilesDir = Path.Combine(HermesHome, "profiles");
        if (Directory.Exists(profilesDir))
        {
            foreach (var dir in Directory.GetDirectories(profilesDir))
            {
                var name = Path.GetFileName(dir);
                profiles.Add(new ProfileInfo
                {
                    Name = name,
                    Path = dir,
                    IsDefault = false,
                    HasEnv = File.Exists(Path.Combine(dir, ".env")),
                    HasConfig = File.Exists(Path.Combine(dir, "config.yaml")),
                    HasSoul = File.Exists(Path.Combine(dir, "SOUL.md")),
                });
            }
        }

        return profiles.OrderBy(p => p.Name == "default" ? 0 : 1).ThenBy(p => p.Name).ToList();
    }

    public static ProfileInfo? GetProfile(string name)
    {
        return GetAllProfiles().FirstOrDefault(p => p.Name == name);
    }

    // Platform-specific tokens that MUST NOT be shared between profiles.
    // Each profile needs its own bot token/app id — sharing causes "token already in use" conflicts.
    private static readonly HashSet<string> PlatformTokenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "WEIXIN_TOKEN", "WEIXIN_ACCOUNT_ID", "WEIXIN_BASE_URL", "WEIXIN_CDN_BASE_URL",
        "QQ_APP_ID", "QQ_CLIENT_SECRET",
        "TELEGRAM_BOT_TOKEN",
        "SLACK_BOT_TOKEN", "SLACK_APP_TOKEN",
        "DISCORD_TOKEN", "DISCORD_CLIENT_ID", "DISCORD_CLIENT_SECRET",
        "TEAMS_APP_ID", "TEAMS_APP_PASSWORD",
        "GOOGLE_CHAT_KEY", "GOOGLE_CHAT_PROJECT_ID",
    };

    public static async Task<ProfileInfo> CreateProfileAsync(string name, Dictionary<string, string>? env = null)
    {
        if (name == "default") throw new ArgumentException("Cannot create profile named 'default'.");
        ValidateProfileName(name);

        var profileDir = GetProfileHome(name);
        if (Directory.Exists(profileDir))
            throw new InvalidOperationException($"Profile '{name}' already exists.");

        Directory.CreateDirectory(profileDir);

        foreach (var sub in new[] { "logs", "sessions", "memories", "workspace", "home", "cron", "pairing", "platforms/pairing" })
            Directory.CreateDirectory(Path.Combine(profileDir, sub));

        // Start with default's .env as base, but EXCLUDE platform-specific tokens
        var baseEnv = LoadEnv("default")
            .Where(kv => !PlatformTokenKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Overlay user-provided diff (which may include new platform tokens for this profile)
        if (env != null)
        {
            foreach (var kv in env)
                baseEnv[kv.Key] = kv.Value;
        }

        if (baseEnv.Count > 0)
        {
            var lines = baseEnv.Select(kv => $"{kv.Key}={kv.Value}");
            await File.WriteAllLinesAsync(Path.Combine(profileDir, ".env"), lines);
        }

        // Also copy config.yaml and SOUL.md from default if they exist
        var defaultDir = GetProfileHome("default");
        var configFile = Path.Combine(defaultDir, "config.yaml");
        if (File.Exists(configFile))
            File.Copy(configFile, Path.Combine(profileDir, "config.yaml"));
        var soulFile = Path.Combine(defaultDir, "SOUL.md");
        if (File.Exists(soulFile))
            File.Copy(soulFile, Path.Combine(profileDir, "SOUL.md"));

        return new ProfileInfo
        {
            Name = name,
            Path = profileDir,
            HasEnv = baseEnv.Count > 0,
        };
    }

    public static async Task CloneProfileAsync(string sourceName, string targetName, bool cloneAll = false)
    {
        ValidateProfileName(targetName);
        var sourceDir = GetProfileHome(sourceName);
        var targetDir = GetProfileHome(targetName);

        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source profile '{sourceName}' not found.");
        if (Directory.Exists(targetDir))
            throw new InvalidOperationException($"Profile '{targetName}' already exists.");

        Directory.CreateDirectory(targetDir);

        foreach (var sub in new[] { "logs", "sessions", "memories", "workspace", "home", "cron", "pairing", "platforms/pairing" })
            Directory.CreateDirectory(Path.Combine(targetDir, sub));

        var envFile = Path.Combine(sourceDir, ".env");
        if (File.Exists(envFile))
            File.Copy(envFile, Path.Combine(targetDir, ".env"));

        if (cloneAll)
        {
            var configFile = Path.Combine(sourceDir, "config.yaml");
            if (File.Exists(configFile))
                File.Copy(configFile, Path.Combine(targetDir, "config.yaml"));

            var soulFile = Path.Combine(sourceDir, "SOUL.md");
            if (File.Exists(soulFile))
                File.Copy(soulFile, Path.Combine(targetDir, "SOUL.md"));
        }
    }

    public static void DeleteProfile(string name)
    {
        if (name == "default") throw new ArgumentException("Cannot delete the default profile.");
        var profileDir = GetProfileHome(name);
        if (!Directory.Exists(profileDir))
            throw new DirectoryNotFoundException($"Profile '{name}' not found.");

        Directory.Delete(profileDir, recursive: true);
    }

    public static async Task SaveEnvValueAsync(string profileName, string key, string value)
    {
        var profileDir = GetProfileHome(profileName);
        var envFile = Path.Combine(profileDir, ".env");
        var lines = new List<string>();

        if (File.Exists(envFile))
            lines = (await File.ReadAllLinesAsync(envFile)).ToList();

        var found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('#')) continue;
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0 && trimmed[..eqIdx].Trim() == key)
            {
                lines[i] = $"{key}={value}";
                found = true;
                break;
            }
        }
        if (!found)
            lines.Add($"{key}={value}");

        await File.WriteAllLinesAsync(envFile, lines);
    }

    public static Dictionary<string, string> LoadEnv(string profileName)
    {
        var profileDir = GetProfileHome(profileName);
        var envFile = Path.Combine(profileDir, ".env");
        var result = new Dictionary<string, string>();

        if (!File.Exists(envFile)) return result;

        foreach (var line in File.ReadAllLines(envFile))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = trimmed[..eqIdx].Trim();
                var val = trimmed[(eqIdx + 1)..].Trim().Trim('"');
                result[key] = val;
            }
        }
        return result;
    }

    public static void ValidateProfileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name cannot be empty.");
        if (name == "default")
            throw new ArgumentException("'default' is a reserved profile name.");
        if (name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Profile name can only contain letters, digits, hyphens, and underscores.");
    }

    /// <summary>
    /// Remove platform-specific tokens from non-default profiles that were incorrectly
    /// inherited from the default profile. These tokens cause "already in use" conflicts.
    /// </summary>
    public static async Task CleanupConflictingTokensAsync()
    {
        foreach (var profile in GetAllProfiles())
        {
            if (profile.IsDefault) continue;

            var envFile = Path.Combine(profile.Path, ".env");
            if (!File.Exists(envFile)) continue;

            var lines = await File.ReadAllLinesAsync(envFile);
            var changed = false;
            var cleanedLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                {
                    cleanedLines.Add(line);
                    continue;
                }
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = trimmed[..eqIdx].Trim();
                    if (PlatformTokenKeys.Contains(key))
                    {
                        changed = true;
                        continue; // Skip this line — remove the conflicting token
                    }
                }
                cleanedLines.Add(line);
            }

            if (changed)
            {
                await File.WriteAllLinesAsync(envFile, cleanedLines);
                App.LogError($"Cleaned conflicting platform tokens from profile '{profile.Name}'");
            }
        }
    }
}
