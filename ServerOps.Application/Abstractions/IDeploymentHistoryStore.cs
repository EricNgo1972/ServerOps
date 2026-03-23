using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IDeploymentHistoryStore
{
    Task<IReadOnlyList<DeploymentHistoryItem>> GetByAppAsync(string appName, CancellationToken ct = default);
    Task AppendAsync(DeploymentHistoryItem item, CancellationToken ct = default);
}
