namespace ServerOps.Application.Abstractions;

public interface IDeploymentPackageValidator
{
    Task<bool> IsValidAsync(string extractedPath, CancellationToken ct = default);
}
