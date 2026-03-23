using System.Text;
using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Deployment;

public sealed class FileLogService : ILogService
{
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public FileLogService(IFileSystem fileSystem, IRuntimeEnvironment runtimeEnvironment)
    {
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public Task<bool> ExistsAsync(string operationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(operationId) && _fileSystem.FileExists(GetLogPath(operationId)));
    }

    public async Task<IReadOnlyList<string>> GetLogLinesAsync(string operationId, int? maxLines = 500, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return Array.Empty<string>();
        }

        var path = GetLogPath(operationId);
        if (!_fileSystem.FileExists(path))
        {
            return Array.Empty<string>();
        }

        var contents = await _fileSystem.ReadAllTextAsync(path, ct);
        if (string.IsNullOrWhiteSpace(contents))
        {
            return Array.Empty<string>();
        }

        var limit = maxLines.GetValueOrDefault(500);
        var lines = contents
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (limit > 0 && lines.Count > limit)
        {
            lines = lines.Skip(lines.Count - limit).ToList();
        }

        return lines;
    }

    private string GetLogPath(string operationId)
    {
        var logDirectory = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), "_logs");
        return _fileSystem.Combine(logDirectory, $"{operationId}.log");
    }
}
