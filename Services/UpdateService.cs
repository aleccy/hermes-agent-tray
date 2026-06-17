using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace HermesAgentTray.Services;

public class UpdateService
{
    private const string RepoApiUrl = "https://api.github.com/repos/aleccy/hermes-agent-tray/releases/latest";
    private static readonly HttpClient _http = new();

    static UpdateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "HermesAgentTray-UpdateChecker");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>
    /// Check GitHub for the latest release. Returns null if up-to-date or on failure.
    /// </summary>
    public async Task<ReleaseInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(RepoApiUrl);
            using var doc = JsonDocument.Parse(json);

            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tag.TrimStart('v');

            if (!Version.TryParse(versionStr, out var remoteVer)) return null;
            if (!Version.TryParse(CurrentVersion, out var localVer)) return null;
            if (remoteVer <= localVer) return null;

            // Find win-x64 zip asset
            string? zipUrl = null;
            string? zipName = null;
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Contains("win-x64") && name.EndsWith(".zip"))
                {
                    zipUrl = asset.GetProperty("browser_download_url").GetString();
                    zipName = name;
                    break;
                }
            }

            if (zipUrl == null) return null;

            return new ReleaseInfo
            {
                Version = versionStr,
                Tag = tag,
                ZipUrl = zipUrl,
                ZipName = zipName ?? $"HermesAgentTray-v{versionStr}-win-x64.zip"
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Download update, extract, and replace current exe via a batch helper.
    /// </summary>
    public async Task<bool> DownloadAndApplyUpdateAsync(ReleaseInfo release, System.Windows.Window owner,
        IProgress<(long downloaded, long total, string status)>? progress = null)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "HermesAgentTray_Update");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, release.ZipName);

            // Download with progress
            progress?.Report((0, 0, Loc.DownloadingUpdate));
            using (var response = await _http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fs = File.Create(zipPath);

                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                var lastReport = DateTime.MinValue;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read);
                    downloaded += read;

                    // Report progress at most every 200ms
                    if (progress != null && (DateTime.Now - lastReport).TotalMilliseconds > 200)
                    {
                        lastReport = DateTime.Now;
                        progress.Report((downloaded, totalBytes, Loc.DownloadingUpdate));
                    }
                }
                progress?.Report((downloaded, totalBytes, Loc.IsZh ? "正在解压..." : "Extracting..."));
            }

            // Extract
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

            // Find the new exe in extracted files
            var newExe = Directory.GetFiles(tempDir, "HermesAgentTray.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (newExe == null) return false;

            var currentExe = Environment.ProcessPath!;
            var stagingExe = Path.Combine(tempDir, "HermesAgentTray.new.exe");
            File.Copy(newExe, stagingExe, overwrite: true);

            // Create updater batch script
            var batPath = Path.Combine(tempDir, "updater.bat");
            var updateMsg = Loc.IsZh ? "正在更新 Hermes Agent Tray..." : "Updating Hermes Agent Tray...";
            var failMsg = Loc.IsZh ? "更新失败" : "Update failed";
            var pid = Environment.ProcessId.ToString();
            var lines = new List<string>
            {
                "@echo off",
                "chcp 65001 >nul",
                $"echo {updateMsg}",
                ":wait",
                $"tasklist /fi \"pid eq {pid}\" 2>nul | find \"{pid}\" >nul",
                "if %errorlevel%==0 (",
                "    timeout /t 1 /nobreak >nul",
                "    goto wait",
                ")",
                $"copy /y \"{stagingExe}\" \"{currentExe}\"",
                "if %errorlevel%==0 (",
                $"    start \"\" \"{currentExe}\"",
                ") else (",
                $"    echo {failMsg}",
                "    pause",
                ")",
                $"rd /s /q \"{tempDir}\"",
                "del \"%~f0\"",
            };
            File.WriteAllLines(batPath, lines);

            // Launch the updater and exit
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class ReleaseInfo
{
    public string Version { get; set; } = "";
    public string Tag { get; set; } = "";
    public string ZipUrl { get; set; } = "";
    public string ZipName { get; set; } = "";
}
