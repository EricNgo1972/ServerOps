using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Deployment;

public sealed class DeploymentPackageValidator : IDeploymentPackageValidator
{
    private readonly IFileSystem _fileSystem;

    public DeploymentPackageValidator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Task<bool> IsValidAsync(string extractedPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(extractedPath) || !_fileSystem.DirectoryExists(extractedPath))
        {
            return Task.FromResult(false);
        }

        var files = new List<string>();
        files.AddRange(_fileSystem.GetFiles(extractedPath, "*.dll", recursive: true));
        files.AddRange(_fileSystem.GetFiles(extractedPath, "*.exe", recursive: true));
        files.AddRange(_fileSystem.GetFiles(extractedPath, "*.deps.json", recursive: true));

        return Task.FromResult(files.Count > 0);
    }
}
