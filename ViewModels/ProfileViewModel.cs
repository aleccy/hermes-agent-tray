using System.ComponentModel;
using System.Runtime.CompilerServices;
using HermesAgentTray.Models;
using HermesAgentTray.Services;

namespace HermesAgentTray.ViewModels;

public class ProfileViewModel : INotifyPropertyChanged
{
    private readonly HermesProcessManager _processManager;
    private ProfileInfo _profile;

    public string Name => _profile.Name;
    public bool IsDefault => _profile.IsDefault;
    public bool CanDelete => !_profile.IsDefault;

    public bool GatewayRunning => _processManager.IsRunning(_profile.Name, ServiceType.Gateway);

    public string GatewayStatusText => GatewayRunning
        ? $"Running (PID {_processManager.GetPid(_profile.Name, ServiceType.Gateway)})"
        : "Stopped";

    public ProfileViewModel(ProfileInfo profile, HermesProcessManager processManager)
    {
        _profile = profile;
        _processManager = processManager;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(GatewayRunning));
        OnPropertyChanged(nameof(GatewayStatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
