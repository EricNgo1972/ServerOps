using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IServiceControlService
{
    Task<CommandResult> StartAsync(string serviceName, string? operationId = null, CancellationToken ct = default);
    Task<CommandResult> StopAsync(string serviceName, string? operationId = null, CancellationToken ct = default);
    Task<CommandResult> RestartAsync(string serviceName, string? operationId = null, CancellationToken ct = default);
}
