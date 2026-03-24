using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IServicePermissionService
{
    Task<CommandResult> EnsureRuntimePermissionsAsync(string serviceName, string deploymentPath, CancellationToken ct = default);
}
