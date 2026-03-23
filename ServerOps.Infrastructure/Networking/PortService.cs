using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Host;

namespace ServerOps.Infrastructure.Networking;

public sealed class PortService : IPortService
{
    private readonly LinuxPortService _linuxPortService;
    private readonly WindowsPortService _windowsPortService;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public PortService(
        LinuxPortService linuxPortService,
        WindowsPortService windowsPortService,
        IRuntimeEnvironment runtimeEnvironment)
    {
        _linuxPortService = linuxPortService;
        _windowsPortService = windowsPortService;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public Task<IReadOnlyList<PortInfo>> GetListeningPortsAsync(CancellationToken cancellationToken = default)
        => _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? _windowsPortService.GetListeningPortsAsync(cancellationToken)
            : _linuxPortService.GetListeningPortsAsync(cancellationToken);
}
