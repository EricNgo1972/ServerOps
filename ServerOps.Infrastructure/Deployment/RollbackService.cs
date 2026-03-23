using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Deployment;

public sealed class RollbackService : IRollbackService
{
    private readonly IDeploymentHistoryStore _deploymentHistoryStore;
    private readonly IServiceControlService _serviceControlService;
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IHealthVerificationService _healthVerificationService;
    private readonly IAppTopologyService _appTopologyService;

    public RollbackService(
        IDeploymentHistoryStore deploymentHistoryStore,
        IServiceControlService serviceControlService,
        IFileSystem fileSystem,
        IRuntimeEnvironment runtimeEnvironment,
        IHealthVerificationService healthVerificationService,
        IAppTopologyService appTopologyService)
    {
        _deploymentHistoryStore = deploymentHistoryStore;
        _serviceControlService = serviceControlService;
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
        _healthVerificationService = healthVerificationService;
        _appTopologyService = appTopologyService;
    }

    public async Task<DeploymentResult> RollbackAsync(string appName, string deploymentId, CancellationToken ct = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var history = await _deploymentHistoryStore.GetByAppAsync(appName, ct);
        var target = history.FirstOrDefault(item =>
            string.Equals(item.DeploymentId, deploymentId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return await AppendRollbackHistoryAsync(new DeploymentResult
            {
                DeploymentId = Guid.NewGuid().ToString("N"),
                AppName = appName,
                Version = "unknown",
                Status = DeploymentStatus.Failed,
                Stage = DeploymentStage.Failed,
                Message = "Deployment history entry was not found.",
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow
            }, null, deploymentId, ct);
        }

        if (target.Status != DeploymentStatus.Succeeded || string.IsNullOrWhiteSpace(target.BackupPath) || !_fileSystem.DirectoryExists(target.BackupPath))
        {
            return await AppendRollbackHistoryAsync(new DeploymentResult
            {
                DeploymentId = Guid.NewGuid().ToString("N"),
                AppName = appName,
                Version = target.Version,
                Status = DeploymentStatus.Failed,
                Stage = DeploymentStage.Failed,
                Message = "Rollback target is invalid or backup is missing.",
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow
            }, target, deploymentId, ct);
        }

        var appRoot = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), appName);
        var currentPath = _fileSystem.Combine(appRoot, "current");
        var tempBackupPath = _fileSystem.Combine(appRoot, $"temp_backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
        var movedCurrent = false;

        try
        {
            var stopResult = await _serviceControlService.StopAsync(appName, ct);
            if (!stopResult.Succeeded)
            {
                return await AppendRollbackHistoryAsync(new DeploymentResult
                {
                    DeploymentId = Guid.NewGuid().ToString("N"),
                    AppName = appName,
                    Version = target.Version,
                    Status = DeploymentStatus.Failed,
                    Stage = DeploymentStage.StoppingService,
                    Message = GetCommandMessage("Failed to stop service.", stopResult),
                    StartedAtUtc = startedAtUtc,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                }, target, deploymentId, ct);
            }

            if (_fileSystem.DirectoryExists(currentPath))
            {
                _fileSystem.MoveDirectory(currentPath, tempBackupPath);
                movedCurrent = true;
            }

            _fileSystem.CopyDirectory(target.BackupPath, currentPath, overwrite: true);

            var startResult = await _serviceControlService.StartAsync(appName, ct);
            if (!startResult.Succeeded)
            {
                return await RecoverFailedRollbackAsync(appName, target, deploymentId, startedAtUtc, currentPath, tempBackupPath, movedCurrent, ct);
            }

            var verified = await VerifyDeploymentWithRetryAsync(appName, ct);
            if (!verified)
            {
                return await RecoverFailedRollbackAsync(appName, target, deploymentId, startedAtUtc, currentPath, tempBackupPath, movedCurrent, ct);
            }

            if (movedCurrent && _fileSystem.DirectoryExists(tempBackupPath))
            {
                _fileSystem.DeleteDirectory(tempBackupPath, recursive: true);
            }

            return await AppendRollbackHistoryAsync(new DeploymentResult
            {
                DeploymentId = Guid.NewGuid().ToString("N"),
                AppName = appName,
                Version = target.Version,
                Status = DeploymentStatus.RolledBack,
                Stage = DeploymentStage.RolledBack,
                Message = "Rollback completed successfully.",
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow
            }, target, deploymentId, ct);
        }
        catch
        {
            return await RecoverFailedRollbackAsync(appName, target, deploymentId, startedAtUtc, currentPath, tempBackupPath, movedCurrent, ct);
        }
    }

    private async Task<DeploymentResult> RecoverFailedRollbackAsync(
        string appName,
        DeploymentHistoryItem target,
        string deploymentId,
        DateTimeOffset startedAtUtc,
        string currentPath,
        string tempBackupPath,
        bool movedCurrent,
        CancellationToken ct)
    {
        try
        {
            if (_fileSystem.DirectoryExists(currentPath))
            {
                _fileSystem.DeleteDirectory(currentPath, recursive: true);
            }

            if (movedCurrent && _fileSystem.DirectoryExists(tempBackupPath))
            {
                _fileSystem.MoveDirectory(tempBackupPath, currentPath);
                await _serviceControlService.StartAsync(appName, ct);
            }
        }
        catch
        {
        }

        return await AppendRollbackHistoryAsync(new DeploymentResult
        {
            DeploymentId = Guid.NewGuid().ToString("N"),
            AppName = appName,
            Version = target.Version,
            Status = DeploymentStatus.Failed,
            Stage = DeploymentStage.Failed,
            Message = "Rollback failed - manual intervention required",
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = DateTimeOffset.UtcNow
        }, target, deploymentId, ct);
    }

    private async Task<bool> VerifyDeploymentWithRetryAsync(string appName, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var topology = await _appTopologyService.GetTopologyAsync(ct);
            var serviceVerified = topology.Any(item =>
                string.Equals(item.ServiceName, appName, StringComparison.OrdinalIgnoreCase) &&
                item.Status == ServiceStatus.Running &&
                item.Ports.Count > 0);

            var healthVerified = serviceVerified && await _healthVerificationService.VerifyAsync(appName, ct);
            if (healthVerified)
            {
                return true;
            }

            if (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        return false;
    }

    private async Task<DeploymentResult> AppendRollbackHistoryAsync(
        DeploymentResult result,
        DeploymentHistoryItem? source,
        string? rolledBackFromDeploymentId,
        CancellationToken ct)
    {
        await _deploymentHistoryStore.AppendAsync(new DeploymentHistoryItem
        {
            DeploymentId = result.DeploymentId,
            AppName = result.AppName,
            Version = result.Version,
            Status = result.Status,
            Stage = result.Stage,
            Message = result.Message,
            StartedAtUtc = result.StartedAtUtc,
            FinishedAtUtc = result.FinishedAtUtc,
            BackupPath = source?.BackupPath,
            IsRollback = true,
            RolledBackFromDeploymentId = rolledBackFromDeploymentId
        }, ct);

        return result;
    }

    private static string GetCommandMessage(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details.Trim()}";
    }
}
