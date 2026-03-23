using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IRollbackService
{
    Task<DeploymentResult> RollbackAsync(string appName, string deploymentId, CancellationToken ct = default);
}
