using ServerOps.Application.Abstractions;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Deployment;

public sealed class DeploymentService : IDeploymentService
{
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly IArchiveService _archiveService;
    private readonly IServiceControlService _serviceControlService;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public DeploymentService(
        IHttpClientFactory httpClientFactory,
        IFileSystem fileSystem,
        IArchiveService archiveService,
        IServiceControlService serviceControlService,
        IRuntimeEnvironment runtimeEnvironment)
    {
        _httpClient = httpClientFactory.CreateClient();
        _fileSystem = fileSystem;
        _archiveService = archiveService;
        _serviceControlService = serviceControlService;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public async Task<DeploymentRecord> DeployAsync(string appName, string assetUrl, CancellationToken cancellationToken = default)
    {
        var record = new DeploymentRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            AppName = appName,
            Version = ExtractVersion(assetUrl),
            Timestamp = DateTimeOffset.UtcNow,
            Status = DeploymentStatus.Running
        };

        var tempFolder = _fileSystem.Combine(_fileSystem.GetTempPath(), "serverops", record.Id);
        var zipPath = _fileSystem.Combine(tempFolder, $"{appName}.zip");
        var extractPath = _fileSystem.Combine(tempFolder, "extracted");

        try
        {
            _fileSystem.CreateDirectory(tempFolder);
            _fileSystem.CreateDirectory(extractPath);

            var bytes = await _httpClient.GetByteArrayAsync(assetUrl, cancellationToken);
            await _fileSystem.WriteAllBytesAsync(zipPath, bytes, cancellationToken);
            await _archiveService.ExtractZipAsync(zipPath, extractPath, cancellationToken);

            var appRoot = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), appName);
            var currentPath = _fileSystem.Combine(appRoot, "current");
            var backupPath = _fileSystem.Combine(appRoot, $"backup_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            var stagingPath = _fileSystem.Combine(appRoot, "staging");

            _fileSystem.CreateDirectory(appRoot);

            var stopResult = await _serviceControlService.StopAsync(appName, cancellationToken);
            if (!stopResult.Succeeded)
            {
                return record with { Status = DeploymentStatus.Failed };
            }

            if (_fileSystem.DirectoryExists(currentPath))
            {
                _fileSystem.CopyDirectory(currentPath, backupPath, overwrite: true);
                _fileSystem.DeleteDirectory(currentPath, recursive: true);
            }

            if (_fileSystem.DirectoryExists(stagingPath))
            {
                _fileSystem.DeleteDirectory(stagingPath, recursive: true);
            }

            _fileSystem.CopyDirectory(extractPath, stagingPath, overwrite: true);
            _fileSystem.MoveDirectory(stagingPath, currentPath);

            var startResult = await _serviceControlService.StartAsync(appName, cancellationToken);
            return record with
            {
                Status = startResult.Succeeded
                    ? DeploymentStatus.Succeeded
                    : DeploymentStatus.Failed
            };
        }
        catch (Exception ex)
        {
            var _ = ex.Message;
            return record with { Status = DeploymentStatus.Failed };
        }
    }

    private static string ExtractVersion(string assetUrl)
    {
        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            return "unknown";
        }

        var fileName = Path.GetFileNameWithoutExtension(assetUrl);
        return string.IsNullOrWhiteSpace(fileName) ? "unknown" : fileName;
    }
}
