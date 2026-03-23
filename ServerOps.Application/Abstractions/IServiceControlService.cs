using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IServiceControlService
{
    Task<CommandResult> StartAsync(string serviceName, CancellationToken ct = default);
    Task<CommandResult> StopAsync(string serviceName, CancellationToken ct = default);
    Task<CommandResult> RestartAsync(string serviceName, CancellationToken ct = default);
}
