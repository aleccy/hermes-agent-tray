using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HermesAgentTray.Models;

namespace HermesAgentTray.Services;

public class HermesProcessManager
{
    private readonly Dictionary<(string profile, ServiceType type), Process> _processes = new();
    private readonly HashSet<(string profile, ServiceType type)> _stopping = new();
    private readonly HashSet<(string profile, ServiceType type)> _starting = new();

    public event Action<string, ServiceType>? ServiceStopped;
    public event Action<string, ServiceType>? ServiceStarted;

    private async Task MonitorProcessExitAsync((string profile, ServiceType type) key, Process process, string profileName, ServiceType type)
    {
        await Task.Run(() => process.WaitForExit());
        _processes.Remove(key);

        var wasStopping = _stopping.Remove(key);
        App.LogError($"Process exited: {profileName} {type} planned={wasStopping}");

        // Always notify UI to refresh; UI won't show error for planned stops
        ServiceStopped?.Invoke(profileName, type);
    }

    public async Task<bool> StartAsync(string profileName, ServiceType type)
    {
        var key = (profileName, type);
        if (_processes.TryGetValue(key, out var existing) && !existing.HasExited)
            return false;

        _starting.Add(key);
        try
        {
            ServiceStarted?.Invoke(profileName, type);
        }
        catch { }

        var profileHome = ProfileManager.GetProfileHome(profileName);
        if (!Directory.Exists(profileHome))
            return false;

        // For Dashboard: stop any existing dashboard on the same port before starting
        if (type == ServiceType.Dashboard)
        {
            var port = GetDashboardPort(profileName);
            if (IsPortInUse(port))
            {
                App.LogError($"Dashboard port {port} in use — killing existing process");
                KillProcessOnPort(port);
                await Task.Delay(2000);
            }
        }

        var (pythonExe, hermesArgs) = FindPythonAndArgs(profileName, type);

        // For Gateway: stop any existing gateway process before starting a new one
        if (type == ServiceType.Gateway)
        {
            await StopExistingGatewayAsync(profileName, profileHome);
        }

        var logDir = Path.Combine(profileHome, "logs");
        Directory.CreateDirectory(logDir);

        var envLines = new List<string>
        {
            $"set HERMES_HOME={profileHome}",
            "set PYTHONIOENCODING=utf-8",
        };
        if (type == ServiceType.Gateway)
            envLines.Add("set HERMES_GATEWAY_DETACHED=1");

        var envFile = Path.Combine(profileHome, ".env");
        if (File.Exists(envFile))
        {
            foreach (var line in await File.ReadAllLinesAsync(envFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var k = trimmed[..eqIdx].Trim();
                    var v = trimmed[(eqIdx + 1)..].Trim().Trim('"');
                    envLines.Add($"set {k}={v}");
                }
            }
        }

        envLines.Add($"\"{pythonExe}\" {hermesArgs}");

        var cmdFile = Path.Combine(logDir, $"_{type.ToString().ToLower()}_starter.cmd");
        try { File.Delete(cmdFile); } catch { }
        await File.WriteAllLinesAsync(cmdFile, envLines);

        var shortCmdFile = GetShortPath(cmdFile);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{shortCmdFile ?? cmdFile}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = profileHome,
        };

        try
        {
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Start();
            _processes[key] = process;
            _starting.Remove(key);
            _ = MonitorProcessExitAsync(key, process, profileName, type);
            App.LogError($"Started {type} for {profileName}: PID={process.Id}");
            return true;
        }
        catch (Exception ex)
        {
            _starting.Remove(key);
            App.LogError($"Failed to start {type} for {profileName}: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync(string profileName, ServiceType type, int timeoutSeconds = 15)
    {
        var key = (profileName, type);

        if (!_processes.TryGetValue(key, out var process))
        {
            // Process not tracked by us — try to stop via CLI or kill by port
            if (type == ServiceType.Dashboard)
            {
                App.LogError($"Dashboard stop: no tracked process for '{profileName}', trying CLI stop");
                await StopDashboardViaCliAsync(profileName);
            }
            else if (type == ServiceType.Desktop)
            {
                KillDesktopProcess();
            }
            return;
        }

        _stopping.Add(key);

        if (type == ServiceType.Gateway)
        {
            // Graceful gateway shutdown: write planned-stop marker + signal via state file
            WritePlannedStopMarker(profileName, process.Id);
            SignalGatewayStop(profileName);

            // Wait for gateway to exit on its own
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited) break;
                await Task.Delay(500);
            }

            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }
        else if (type == ServiceType.Dashboard)
        {
            // Use CLI to gracefully stop dashboard
            _processes.Remove(key);
            _stopping.Remove(key);
            await StopDashboardViaCliAsync(profileName);
            return;
        }
        else
        {
            // Desktop: kill just the cmd.exe process (not entire tree)
            try { process.Kill(); } catch { }
        }

        _processes.Remove(key);
        _stopping.Remove(key);
    }

    private void WritePlannedStopMarker(string profileName, int pid)
    {
        var profileHome = ProfileManager.GetProfileHome(profileName);
        var markerPath = Path.Combine(profileHome, ".gateway-planned-stop.json");
        try
        {
            var record = new
            {
                target_pid = pid,
                target_start_time = (string?)null,
                stopper_pid = Process.GetCurrentProcess().Id,
                written_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"),
            };
            File.WriteAllText(markerPath, JsonSerializer.Serialize(record));
        }
        catch (Exception ex)
        {
            App.LogError($"Failed to write planned-stop marker: {ex.Message}");
        }
    }

    /// <summary>
    /// Write gateway_state.json with "stopped" to signal the gateway to shut down.
    /// The gateway watches this file and exits cleanly when it sees "stopped".
    /// </summary>
    private void SignalGatewayStop(string profileName)
    {
        var profileHome = ProfileManager.GetProfileHome(profileName);
        var stateFile = Path.Combine(profileHome, "gateway_state.json");
        try
        {
            // Read existing state to preserve fields
            JsonElement? existing = null;
            if (File.Exists(stateFile))
            {
                try { existing = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(stateFile)); } catch { }
            }

            var dict = new Dictionary<string, object>();
            if (existing.HasValue)
            {
                foreach (var prop in existing.Value.EnumerateObject())
                {
                    if (prop.Name != "gateway_state")
                        dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
                }
            }
            dict["gateway_state"] = "stopped";
            dict["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

            File.WriteAllText(stateFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            App.LogError($"Failed to signal gateway stop: {ex.Message}");
        }
    }

    public async Task StopAllForProfileAsync(string profileName)
    {
        foreach (ServiceType type in Enum.GetValues(typeof(ServiceType)))
            await StopAsync(profileName, type);
    }

    public async Task StopAllAsync()
    {
        var keys = _processes.Keys.ToList();
        await Task.WhenAll(keys.Select(k => StopAsync(k.profile, k.type)));
    }

    public bool IsRunning(string profileName, ServiceType type)
    {
        var key = (profileName, type);
        return _processes.TryGetValue(key, out var p) && !p.HasExited;
    }

    public bool IsStarting(string profileName, ServiceType type)
    {
        var key = (profileName, type);
        return _starting.Contains(key);
    }

    public bool IsStartingAny(ServiceType type)
    {
        return _starting.Any(k => k.type == type);
    }

    public int? GetPid(string profileName, ServiceType type)
    {
        var key = (profileName, type);
        return _processes.TryGetValue(key, out var p) && !p.HasExited ? p.Id : null;
    }

    /// <summary>
    /// Find the profile name of a currently running service of the given type.
    /// Returns null if no such service is running.
    /// </summary>
    public string? GetRunningProfile(ServiceType type)
    {
        foreach (var kv in _processes)
        {
            if (kv.Key.type == type && !kv.Value.HasExited)
                return kv.Key.profile;
        }
        return null;
    }

    /// <summary>
    /// Check if any dashboard is running (port-based detection).
    /// Dashboard is a global singleton — only one can run at a time.
    /// </summary>
    public bool IsDashboardRunning()
    {
        // First check our tracked processes
        foreach (var kv in _processes)
        {
            if (kv.Key.type == ServiceType.Dashboard && !kv.Value.HasExited)
                return true;
        }
        // Also check port 9119 directly (dashboard may have been started externally)
        var port = GetDashboardPort("default");
        return IsPortInUse(port);
    }

    /// <summary>
    /// Find which profile the running dashboard belongs to.
    /// Returns null if no dashboard is running.
    /// </summary>
    public string? GetDashboardRunningProfile()
    {
        foreach (var kv in _processes)
        {
            if (kv.Key.type == ServiceType.Dashboard && !kv.Value.HasExited)
                return kv.Key.profile;
        }
        // If running externally, assume default
        if (IsDashboardRunning())
            return "default";
        return null;
    }

    public int GetDashboardPort(string profileName)
    {
        var env = ProfileManager.LoadEnv(profileName);
        if (env.TryGetValue("DASHBOARD_PORT", out var portStr) && int.TryParse(portStr, out var p) && p > 0)
            return p;
        return 9119;
    }

    public GatewayStateInfo? GetGatewayState(string profileName)
    {
        var profileHome = ProfileManager.GetProfileHome(profileName);
        var stateFile = Path.Combine(profileHome, "gateway_state.json");
        if (!File.Exists(stateFile)) return null;

        try
        {
            var json = File.ReadAllText(stateFile);
            var el = JsonSerializer.Deserialize<JsonElement>(json);
            var info = new GatewayStateInfo();
            if (el.TryGetProperty("gateway_state", out var gs)) info.State = gs.GetString();
            if (el.TryGetProperty("active_agents", out var aa)) info.ActiveAgents = aa.GetInt32();
            if (el.TryGetProperty("platforms", out var ps))
                info.Platforms = ps.EnumerateObject().Select(p => p.Name).ToList();
            if (el.TryGetProperty("updated_at", out var ua)) info.UpdatedAt = ua.GetString();
            return info;
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferSize);

    /// <summary>
    /// Stop any existing gateway process (e.g. started outside HermesAgentTray)
    /// by running "hermes gateway stop" before starting a new one.
    /// </summary>
    private async Task StopExistingGatewayAsync(string profileName, string profileHome)
    {
        // Clean up stale state files
        try { File.Delete(Path.Combine(profileHome, "gateway_state.json")); } catch { }
        try { File.Delete(Path.Combine(profileHome, ".gateway-planned-stop.json")); } catch { }

        // Use hermes CLI to stop any existing gateway
        var hermesExe = FindHermesExe();
        var psi = new ProcessStartInfo
        {
            FileName = hermesExe,
            Arguments = profileName == "default" ? "gateway stop" : $"-p {profileName} gateway stop",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = profileHome,
        };
        psi.EnvironmentVariables["HERMES_HOME"] = profileHome;

        try
        {
            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                App.LogError($"Stopped existing gateway for {profileName}: exit={proc.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            App.LogError($"StopExistingGateway failed (may be no existing gateway): {ex.Message}");
        }

        // Wait a moment for the process to fully exit
        await Task.Delay(2000);

        // Clean up state files again after stop
        try { File.Delete(Path.Combine(profileHome, "gateway_state.json")); } catch { }
        try { File.Delete(Path.Combine(profileHome, ".gateway-planned-stop.json")); } catch { }
    }

    private static string? GetShortPath(string longPath)
    {
        try
        {
            var buffer = new StringBuilder(260);
            if (GetShortPathName(longPath, buffer, buffer.Capacity) > 0)
                return buffer.ToString();
        }
        catch { }
        return null;
    }

    private static (string exe, string args) FindPythonAndArgs(string profileName, ServiceType type)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var venvPython = Path.Combine(localAppData, "hermes", "hermes-agent", "venv", "Scripts", "python.exe");

        string exe;
        if (File.Exists(venvPython))
            exe = venvPython;
        else
            exe = "python.exe";

        var profileArg = profileName == "default" ? "" : $"-p {profileName}";
        var subcmd = type switch
        {
            ServiceType.Gateway => $"{profileArg} gateway run",
            ServiceType.Desktop => $"{profileArg} desktop",
            ServiceType.Dashboard => $"{profileArg} dashboard --no-open",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        return (exe, $"-m hermes_cli.main {subcmd}");
    }

    private static string FindHermesExe()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "hermes.exe");
            if (File.Exists(candidate)) return candidate;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installed = Path.Combine(localAppData, "hermes", "hermes-agent", "venv", "Scripts", "hermes.exe");
        if (File.Exists(installed)) return installed;

        return "hermes.exe";
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(1000);
            if (connected)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stop dashboard: try CLI first, then kill by port as fallback.
    /// </summary>
    private async Task StopDashboardViaCliAsync(string profileName)
    {
        var port = GetDashboardPort(profileName);

        // Step 1: Try CLI stop
        var hermesExe = FindHermesExe();
        if (hermesExe != null)
        {
            var profileHome = ProfileManager.GetProfileHome(profileName);
            var psi = new ProcessStartInfo
            {
                FileName = hermesExe,
                Arguments = profileName == "default" ? "dashboard --stop" : $"-p {profileName} dashboard --stop",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = profileHome,
            };
            psi.EnvironmentVariables["HERMES_HOME"] = profileHome;

            try
            {
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    App.LogError($"Dashboard --stop exited with code {proc.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                App.LogError($"Dashboard CLI stop failed: {ex.Message}");
            }

            // Wait and check if port is freed
            await Task.Delay(2000);
            if (!IsPortInUse(port))
            {
                App.LogError($"Dashboard stopped successfully via CLI");
                return;
            }
            App.LogError($"Dashboard CLI stop didn't free port {port}, trying KillProcessOnPort");
        }

        // Step 2: Force kill by port
        if (IsPortInUse(port))
        {
            App.LogError($"Killing dashboard process on port {port}");
            KillProcessOnPort(port);
            await Task.Delay(2000);
            App.LogError($"After KillProcessOnPort, port {port} still in use: {IsPortInUse(port)}");
        }
    }

    /// <summary>
    /// Kill any running Desktop (electron) process that we didn't start.
    /// </summary>
    private static void KillDesktopProcess()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("hermes-desktop"))
            {
                App.LogError($"Killing untracked desktop process: PID={proc.Id}");
                proc.Kill();
            }
            foreach (var proc in Process.GetProcessesByName("Hermes Desktop"))
            {
                App.LogError($"Killing untracked desktop process: PID={proc.Id}");
                proc.Kill();
            }
        }
        catch (Exception ex)
        {
            App.LogError($"KillDesktopProcess failed: {ex.Message}");
        }
    }

    private static void KillProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            var output = Process.Start(psi)?.StandardOutput.ReadToEnd();
            if (string.IsNullOrEmpty(output)) return;

            foreach (var line in output.Split('\n'))
            {
                // Look for LISTENING on the target port
                if (!line.Contains("LISTENING")) continue;
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var localAddr = parts[1];
                if (!localAddr.EndsWith($":{port}")) continue;

                var pidStr = parts[^1];
                if (!int.TryParse(pidStr, out var pid) || pid == 0) continue;

                try
                {
                    var proc = Process.GetProcessById(pid);
                    App.LogError($"Killing process {proc.ProcessName} (PID {pid}) on port {port}");
                    proc.Kill();
                }
                catch (Exception ex)
                {
                    App.LogError($"Failed to kill PID {pid}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            App.LogError($"KillProcessOnPort failed: {ex.Message}");
        }
    }
}
