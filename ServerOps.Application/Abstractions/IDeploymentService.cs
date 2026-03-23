using ServerOps.Domain.Entities;

namespace ServerOps.Application.Abstractions;

public interface IDeploymentService
{
    Task<DeploymentRecord> DeployAsync(string appName, string assetUrl, CancellationToken cancellationToken = default);
}
