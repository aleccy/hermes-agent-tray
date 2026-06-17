using System.Net.Sockets;
using HermesAgentTray.Models;

namespace HermesAgentTray.Services;

public class HealthMonitor : IDisposable
{
    private readonly HermesProcessManager _processManager;
    private System.Threading.Timer? _timer;
    private readonly Dictionary<string, bool> _lastHealth = new();

    /// <summary>
    /// Fired when a gateway's health status changes. (profileName, isHealthy)
    /// </summary>
    public event Action<string, bool>? GatewayHealthChanged;

    /// <summary>
    /// Get current health status. Returns null if not checked yet.
    /// </summary>
    public bool? IsHealthy(string profileName)
        => _lastHealth.TryGetValue(profileName, out var h) ? h : null;

    public HealthMonitor(HermesProcessManager processManager)
    {
        _processManager = processManager;
    }

    public void Start(int intervalMs = 30000)
    {
        _timer = new System.Threading.Timer(_ => CheckAll(), null, intervalMs, intervalMs);
    }

    private void CheckAll()
    {
        var profiles = ProfileManager.GetAllProfiles();
        foreach (var profile in profiles)
        {
            if (!_processManager.IsRunning(profile.Name, ServiceType.Gateway))
            {
                if (_lastHealth.ContainsKey(profile.Name))
                    _lastHealth.Remove(profile.Name);
                continue;
            }

            var port = GetGatewayPort(profile.Name);
            if (port <= 0) continue;

            // TCP port probe — just check if the port is accepting connections
            bool healthy = IsPortListening(port);

            var wasHealthy = _lastHealth.TryGetValue(profile.Name, out var prev) ? (bool?)prev : null;
            _lastHealth[profile.Name] = healthy;

            // Only notify on state change
            if (wasHealthy != healthy)
            {
                GatewayHealthChanged?.Invoke(profile.Name, healthy);
            }
        }
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(2000);
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

    private static int GetGatewayPort(string profileName)
    {
        var env = ProfileManager.LoadEnv(profileName);
        if (env.TryGetValue("GATEWAY_PORT", out var portStr) && int.TryParse(portStr, out var p) && p > 0)
            return p;
        // Try common gateway port env vars
        if (env.TryGetValue("HERMES_GATEWAY_PORT", out var hp) && int.TryParse(hp, out var h) && h > 0)
            return h;
        if (env.TryGetValue("PORT", out var pp) && int.TryParse(pp, out var pn) && pn > 0)
            return pn;
        return 0; // Unknown port — skip health check
    }

    public void Dispose() => _timer?.Dispose();
}
