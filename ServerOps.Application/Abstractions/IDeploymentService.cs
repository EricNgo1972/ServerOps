using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IDeploymentService
{
    Task<DeploymentResult> DeployAsync(string appName, string assetUrl, CancellationToken cancellationToken = default);
}
