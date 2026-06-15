namespace HermesAgentTray.Models;

public enum ServiceType { Gateway, Desktop, Dashboard }

public enum ServiceStatus { Stopped, Starting, Running, Stopping }

public class ProfileInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool HasEnv { get; set; }
    public bool HasConfig { get; set; }
    public bool HasSoul { get; set; }
    public string? Description { get; set; }
    public Dictionary<ServiceType, ServiceStatus> ServiceStatuses { get; set; } = new();
    public Dictionary<ServiceType, int> ServicePids { get; set; } = new();
    public Dictionary<string, string> EnvValues { get; set; } = new();
    public GatewayStateInfo? GatewayState { get; set; }
}

public class GatewayStateInfo
{
    public string? State { get; set; }
    public int ActiveAgents { get; set; }
    public List<string> Platforms { get; set; } = new();
    public string? UpdatedAt { get; set; }
}

public class UsageSummary
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public double EstimatedCostUsd { get; set; }
    public int SessionCount { get; set; }
}
