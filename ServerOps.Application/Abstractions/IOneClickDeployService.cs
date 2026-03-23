using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IOneClickDeployService
{
    Task<OneClickDeployResult> DeployAsync(OneClickDeployRequest request, CancellationToken ct = default);
}
