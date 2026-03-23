using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface ICloudflaredService
{
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);
    Task<TunnelInfo> GetTunnelInfoAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> InstallAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> RestartAsync(CancellationToken cancellationToken = default);
}
