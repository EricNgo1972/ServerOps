namespace ServerOps.Application.Abstractions;

public interface IArchiveService
{
    Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken = default);
}
