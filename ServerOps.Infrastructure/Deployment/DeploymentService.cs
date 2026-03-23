using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Deployment;

public sealed class DeploymentService : IDeploymentService
{
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly IArchiveService _archiveService;
    private readonly IServiceControlService _serviceControlService;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IDeploymentPackageValidator _deploymentPackageValidator;
    private readonly IHealthVerificationService _healthVerificationService;
    private readonly IAppTopologyService _appTopologyService;

    public DeploymentService(
        IHttpClientFactory httpClientFactory,
        IFileSystem fileSystem,
        IArchiveService archiveService,
        IServiceControlService serviceControlService,
        IRuntimeEnvironment runtimeEnvironment,
        IDeploymentPackageValidator deploymentPackageValidator,
        IHealthVerificationService healthVerificationService,
        IAppTopologyService appTopologyService)
    {
        _httpClient = httpClientFactory.CreateClient();
        _fileSystem = fileSystem;
        _archiveService = archiveService;
        _serviceControlService = serviceControlService;
        _runtimeEnvironment = runtimeEnvironment;
        _deploymentPackageValidator = deploymentPackageValidator;
        _healthVerificationService = healthVerificationService;
        _appTopologyService = appTopologyService;
    }

    public async Task<DeploymentResult> DeployAsync(string appName, string assetUrl, CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var deploymentId = Guid.NewGuid().ToString("N");
        var version = ExtractVersion(assetUrl);

        if (string.IsNullOrWhiteSpace(appName))
        {
            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                "Application name is required.",
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }

        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                "Asset URL is required.",
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }

        var tempFolder = _fileSystem.Combine(_fileSystem.GetTempPath(), "serverops", deploymentId);
        var zipPath = _fileSystem.Combine(tempFolder, $"{appName}.zip");
        var appRoot = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), appName);
        var currentPath = _fileSystem.Combine(appRoot, "current");
        var backupPath = _fileSystem.Combine(appRoot, $"backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
        var stagingPath = _fileSystem.Combine(appRoot, "staging");
        var hadCurrent = _fileSystem.DirectoryExists(currentPath);

        try
        {
            _fileSystem.CreateDirectory(tempFolder);
            _fileSystem.CreateDirectory(appRoot);

            var bytes = await DownloadAsync(assetUrl, cancellationToken);
            await _fileSystem.WriteAllBytesAsync(zipPath, bytes, cancellationToken);

            if (_fileSystem.DirectoryExists(stagingPath))
            {
                _fileSystem.DeleteDirectory(stagingPath, recursive: true);
            }

            _fileSystem.CreateDirectory(stagingPath);
            await _archiveService.ExtractZipAsync(zipPath, stagingPath, cancellationToken);

            var packageIsValid = await _deploymentPackageValidator.IsValidAsync(stagingPath, cancellationToken);
            if (!packageIsValid)
            {
                return CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.ValidatingPackage,
                    "Deployment package validation failed.",
                    startedAtUtc,
                    DateTimeOffset.UtcNow);
            }

            var rollbackAvailable = false;
            var stopResult = await _serviceControlService.StopAsync(appName, cancellationToken);
            if (!stopResult.Succeeded)
            {
                return CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.StoppingService,
                    GetCommandMessage("Failed to stop service.", stopResult),
                    startedAtUtc,
                    DateTimeOffset.UtcNow);
            }

            try
            {
                if (hadCurrent)
                {
                    if (_fileSystem.DirectoryExists(backupPath))
                    {
                        _fileSystem.DeleteDirectory(backupPath, recursive: true);
                    }

                    _fileSystem.MoveDirectory(currentPath, backupPath);
                    rollbackAvailable = true;
                }

                _fileSystem.MoveDirectory(stagingPath, currentPath);
            }
            catch (Exception ex)
            {
                if (rollbackAvailable)
                {
                    return await RollbackAsync(
                        deploymentId,
                        appName,
                        version,
                        startedAtUtc,
                        backupPath,
                        currentPath,
                        rollbackAvailable,
                        $"Activation failed: {ex.Message}",
                        cancellationToken);
                }

                return CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.ActivatingNewVersion,
                    ex.Message,
                    startedAtUtc,
                    DateTimeOffset.UtcNow);
            }

            var startResult = await _serviceControlService.StartAsync(appName, cancellationToken);
            if (!startResult.Succeeded)
            {
                return await RollbackAsync(
                    deploymentId,
                    appName,
                    version,
                    startedAtUtc,
                    backupPath,
                    currentPath,
                    rollbackAvailable,
                    GetCommandMessage("Failed to start service.", startResult),
                    cancellationToken);
            }

            var serviceVerified = await VerifyServiceAsync(appName, cancellationToken);
            if (!serviceVerified)
            {
                return await RollbackAsync(
                    deploymentId,
                    appName,
                    version,
                    startedAtUtc,
                    backupPath,
                    currentPath,
                    rollbackAvailable,
                    "Service verification failed after deployment.",
                    cancellationToken);
            }

            var healthVerified = await VerifyHealthWithRetryAsync(appName, cancellationToken);
            if (!healthVerified)
            {
                return await RollbackAsync(
                    deploymentId,
                    appName,
                    version,
                    startedAtUtc,
                    backupPath,
                    currentPath,
                    rollbackAvailable,
                    "Health verification failed after deployment.",
                    cancellationToken);
            }

            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Succeeded,
                DeploymentStage.Completed,
                "Deployment completed successfully.",
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                ex.Message,
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task<byte[]> DownloadAsync(string assetUrl, CancellationToken cancellationToken)
    {
        return await _httpClient.GetByteArrayAsync(assetUrl, cancellationToken);
    }

    private async Task<bool> VerifyServiceAsync(string appName, CancellationToken cancellationToken)
    {
        var topology = await _appTopologyService.GetTopologyAsync(cancellationToken);
        return topology.Any(item =>
            string.Equals(item.ServiceName, appName, StringComparison.OrdinalIgnoreCase) &&
            item.Status == ServiceStatus.Running &&
            item.Ports.Count > 0);
    }

    private async Task<bool> VerifyHealthWithRetryAsync(string appName, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await _healthVerificationService.VerifyAsync(appName, cancellationToken))
            {
                return true;
            }

            if (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        return false;
    }

    private async Task<bool> VerifyServiceWithRetryAsync(string appName, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await VerifyServiceAsync(appName, cancellationToken))
            {
                return true;
            }

            if (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        return false;
    }

    private async Task<DeploymentResult> RollbackAsync(
        string deploymentId,
        string appName,
        string version,
        DateTimeOffset startedAtUtc,
        string backupPath,
        string currentPath,
        bool hasBackup,
        string message,
        CancellationToken cancellationToken)
    {
        if (!hasBackup || !_fileSystem.DirectoryExists(backupPath))
        {
            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                $"{message} Rollback failed because no backup is available.",
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }

        try
        {
            if (_fileSystem.DirectoryExists(currentPath))
            {
                _fileSystem.DeleteDirectory(currentPath, recursive: true);
            }

            _fileSystem.MoveDirectory(backupPath, currentPath);

            var restartResult = await _serviceControlService.StartAsync(appName, cancellationToken);
            if (!restartResult.Succeeded)
            {
                return CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.Failed,
                    $"{message} Rollback restart failed. {GetCommandMessage("Service start failed.", restartResult)}",
                    startedAtUtc,
                    DateTimeOffset.UtcNow);
            }

            var serviceVerified = await VerifyServiceWithRetryAsync(appName, cancellationToken);
            if (!serviceVerified)
            {
                return CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.Failed,
                    $"{message} Rollback verification failed.",
                    startedAtUtc,
                    DateTimeOffset.UtcNow);
            }

            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.RolledBack,
                DeploymentStage.RolledBack,
                $"{message} Rollback completed successfully.",
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                $"{message} Rollback failed: {ex.Message}",
                startedAtUtc,
                DateTimeOffset.UtcNow);
        }
    }

    private static DeploymentResult CreateResult(
        string deploymentId,
        string appName,
        string version,
        DeploymentStatus status,
        DeploymentStage stage,
        string? message,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? finishedAtUtc)
    {
        return new DeploymentResult
        {
            DeploymentId = deploymentId,
            AppName = appName,
            Version = version,
            Status = status,
            Stage = stage,
            Message = message,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = finishedAtUtc
        };
    }

    private static string GetCommandMessage(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details.Trim()}";
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
