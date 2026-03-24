using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IServiceRegistrationService
{
    Task<bool> ExistsAsync(string serviceName, CancellationToken ct = default);
    Task<CommandResult> RegisterAsync(string serviceName, string deploymentPath, CancellationToken ct = default);
    Task<CommandResult> UnregisterAsync(string serviceName, CancellationToken ct = default);
}
