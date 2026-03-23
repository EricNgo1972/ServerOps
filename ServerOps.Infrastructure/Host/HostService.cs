using ServerOps.Application.Abstractions;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host;

public sealed class HostService : IHostService
{
    private readonly LinuxHostService _linuxHostService;
    private readonly WindowsHostService _windowsHostService;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public HostService(
        LinuxHostService linuxHostService,
        WindowsHostService windowsHostService,
        IRuntimeEnvironment runtimeEnvironment)
    {
        _linuxHostService = linuxHostService;
        _windowsHostService = windowsHostService;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public OsType GetCurrentOs() => _runtimeEnvironment.GetCurrentOs();

    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default)
    {
        var os = GetCurrentOs();
        return os == OsType.Windows
            ? _windowsHostService.GetServicesAsync(ct)
            : _linuxHostService.GetServicesAsync(ct);
    }
}
