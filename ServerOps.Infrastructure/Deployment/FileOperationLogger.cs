using System.Text;
using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Deployment;

public sealed class FileOperationLogger : IOperationLogger
{
    private static readonly SemaphoreSlim LogLock = new(1, 1);
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public FileOperationLogger(IFileSystem fileSystem, IRuntimeEnvironment runtimeEnvironment)
    {
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public async Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("Operation ID is required.", nameof(operationId));
        }

        var logDirectory = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), "_logs");
        var logPath = _fileSystem.Combine(logDirectory, $"{operationId}.log");
        var line = $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} [{stage}] {message}";

        await LogLock.WaitAsync(ct);
        try
        {
            _fileSystem.CreateDirectory(logDirectory);
            var existing = _fileSystem.FileExists(logPath)
                ? await _fileSystem.ReadAllTextAsync(logPath, ct)
                : string.Empty;
            var content = string.IsNullOrWhiteSpace(existing)
                ? line + Environment.NewLine
                : existing + line + Environment.NewLine;
            await _fileSystem.WriteAllBytesAsync(logPath, Encoding.UTF8.GetBytes(content), ct);
        }
        finally
        {
            LogLock.Release();
        }
    }
}
