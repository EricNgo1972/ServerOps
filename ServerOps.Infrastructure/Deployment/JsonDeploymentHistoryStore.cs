using System.Text;
using System.Text.Json;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.Deployment;

public sealed class JsonDeploymentHistoryStore : IDeploymentHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public JsonDeploymentHistoryStore(IFileSystem fileSystem, IRuntimeEnvironment runtimeEnvironment)
    {
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public async Task<IReadOnlyList<DeploymentHistoryItem>> GetByAppAsync(string appName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return Array.Empty<DeploymentHistoryItem>();
        }

        var path = GetHistoryPath(appName);
        if (!_fileSystem.FileExists(path))
        {
            await InitializeFileAsync(path, ct);
            return Array.Empty<DeploymentHistoryItem>();
        }

        var contents = await _fileSystem.ReadAllTextAsync(path, ct);
        if (string.IsNullOrWhiteSpace(contents))
        {
            return Array.Empty<DeploymentHistoryItem>();
        }

        var items = JsonSerializer.Deserialize<List<DeploymentHistoryItem>>(contents, JsonOptions);
        return items is null
            ? Array.Empty<DeploymentHistoryItem>()
            : items;
    }

    public async Task AppendAsync(DeploymentHistoryItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var path = GetHistoryPath(item.AppName);
        var items = (await GetByAppAsync(item.AppName, ct)).ToList();
        items.Add(item);

        _fileSystem.CreateDirectory(GetHistoryDirectory());
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items, JsonOptions));
        await _fileSystem.WriteAllBytesAsync(path, bytes, ct);
    }

    private async Task InitializeFileAsync(string path, CancellationToken ct)
    {
        _fileSystem.CreateDirectory(GetHistoryDirectory());
        await _fileSystem.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("[]"), ct);
    }

    private string GetHistoryDirectory()
        => _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), "_history");

    private string GetHistoryPath(string appName)
        => _fileSystem.Combine(GetHistoryDirectory(), $"{appName}.json");
}
