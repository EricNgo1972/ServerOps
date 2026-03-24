using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface ICloudflaredService
{
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);
    Task<TunnelInfo> GetTunnelInfoAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> InstallAsync(string? operationId = null, CancellationToken cancellationToken = default);
    Task<CommandResult> CreateTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default);
    Task<CommandResult> StartAsync(string? operationId = null, CancellationToken cancellationToken = default);
    Task<CommandResult> RestartAsync(string? operationId = null, CancellationToken cancellationToken = default);
    Task<CommandResult> DeleteTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default);
}
