using System.IO.Compression;
using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Deployment;

public sealed class ZipArchiveService : IArchiveService
{
    public Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ZipFile.ExtractToDirectory(zipPath, destinationPath, overwriteFiles: true);
        return Task.CompletedTask;
    }
}
