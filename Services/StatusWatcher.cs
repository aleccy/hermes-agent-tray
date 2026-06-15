using System.Text.Json;
using HermesAgentTray.Models;
using HermesAgentTray.Services;

namespace HermesAgentTray.Services;

public class StatusWatcher : IDisposable
{
    private readonly HermesProcessManager _processManager;
    private System.Threading.Timer? _timer;
    private readonly Dictionary<string, ProfileInfo> _lastStatus = new();

    public event Action<string, ProfileInfo>? ProfileStatusChanged;

    public StatusWatcher(HermesProcessManager processManager)
    {
        _processManager = processManager;
    }

    public void Start(int intervalMs = 5000)
    {
        _timer = new System.Threading.Timer(_ => PollAll(), null, 0, intervalMs);
    }

    private void PollAll()
    {
        var profiles = ProfileManager.GetAllProfiles();
        foreach (var profile in profiles)
        {
            var updated = new ProfileInfo
            {
                Name = profile.Name,
                Path = profile.Path,
                IsDefault = profile.IsDefault,
                HasEnv = profile.HasEnv,
                HasConfig = profile.HasConfig,
                HasSoul = profile.HasSoul,
            };

            // Update service statuses
            foreach (ServiceType type in Enum.GetValues(typeof(ServiceType)))
            {
                var running = _processManager.IsRunning(profile.Name, type);
                updated.ServiceStatuses[type] = running ? ServiceStatus.Running : ServiceStatus.Stopped;
                var pid = _processManager.GetPid(profile.Name, type);
                if (pid.HasValue) updated.ServicePids[type] = pid.Value;
            }

            // Update gateway state from file
            updated.GatewayState = _processManager.GetGatewayState(profile.Name);

            // Check for changes
            var changed = !_lastStatus.TryGetValue(profile.Name, out var last) ||
                !ServiceStatusesEqual(last.ServiceStatuses, updated.ServiceStatuses) ||
                last.GatewayState?.State != updated.GatewayState?.State ||
                last.GatewayState?.ActiveAgents != updated.GatewayState?.ActiveAgents;

            if (changed)
            {
                _lastStatus[profile.Name] = updated;
                ProfileStatusChanged?.Invoke(profile.Name, updated);
            }
        }
    }

    private static bool ServiceStatusesEqual(Dictionary<ServiceType, ServiceStatus> a, Dictionary<ServiceType, ServiceStatus> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var val) || val != kv.Value)
                return false;
        return true;
    }

    public void Dispose() => _timer?.Dispose();
}
