using System.Text;
using System.Text.Json;
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
    private readonly IServiceRegistrationService _serviceRegistrationService;
    private readonly IServicePermissionService _servicePermissionService;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IDeploymentPackageValidator _deploymentPackageValidator;
    private readonly IHealthVerificationService _healthVerificationService;
    private readonly IAppTopologyService _appTopologyService;
    private readonly IDeploymentHistoryStore _deploymentHistoryStore;
    private readonly IOperationLogger _operationLogger;

    public DeploymentService(
        IHttpClientFactory httpClientFactory,
        IFileSystem fileSystem,
        IArchiveService archiveService,
        IServiceControlService serviceControlService,
        IServiceRegistrationService serviceRegistrationService,
        IServicePermissionService servicePermissionService,
        IRuntimeEnvironment runtimeEnvironment,
        IDeploymentPackageValidator deploymentPackageValidator,
        IHealthVerificationService healthVerificationService,
        IAppTopologyService appTopologyService,
        IDeploymentHistoryStore deploymentHistoryStore,
        IOperationLogger operationLogger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _fileSystem = fileSystem;
        _archiveService = archiveService;
        _serviceControlService = serviceControlService;
        _serviceRegistrationService = serviceRegistrationService;
        _servicePermissionService = servicePermissionService;
        _runtimeEnvironment = runtimeEnvironment;
        _deploymentPackageValidator = deploymentPackageValidator;
        _healthVerificationService = healthVerificationService;
        _appTopologyService = appTopologyService;
        _deploymentHistoryStore = deploymentHistoryStore;
        _operationLogger = operationLogger;
    }

    public async Task<DeploymentResult> DeployAsync(string appName, string assetUrl, int? portOverride = null, string? operationId = null, CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var deploymentId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        var version = ExtractVersion(assetUrl);

        if (string.IsNullOrWhiteSpace(appName))
        {
            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                "Application name is required.",
                startedAtUtc,
                DateTimeOffset.UtcNow), null, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                "Asset URL is required.",
                startedAtUtc,
                    DateTimeOffset.UtcNow), null, cancellationToken);
        }

        if (portOverride is <= 0 or > 65535)
        {
            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                "Port override must be between 1 and 65535.",
                startedAtUtc,
                DateTimeOffset.UtcNow), null, cancellationToken);
        }

        var tempFolder = _fileSystem.Combine(_fileSystem.GetTempPath(), "serverops", deploymentId);
        var zipPath = _fileSystem.Combine(tempFolder, $"{appName}.zip");
        var appRoot = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), appName);
        var currentPath = _fileSystem.Combine(appRoot, "current");
        var currentTempPath = _fileSystem.Combine(appRoot, "current_tmp");
        var backupPath = _fileSystem.Combine(appRoot, $"backup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
        var stagingPath = _fileSystem.Combine(appRoot, "staging");
        var hadCurrent = _fileSystem.DirectoryExists(currentPath);

        try
        {
            await LogStageAsync(
                deploymentId,
                "Deployment",
                $"Started app={appName}, version={version}, assetUrl={assetUrl}",
                cancellationToken);
            await LogStageAsync(
                deploymentId,
                "Deployment",
                $"Paths temp={tempFolder}, zip={zipPath}, appRoot={appRoot}, current={currentPath}, backup={backupPath}, staging={stagingPath}, hadCurrent={hadCurrent}",
                cancellationToken);
            _fileSystem.CreateDirectory(tempFolder);
            _fileSystem.CreateDirectory(appRoot);

            await LogStageAsync(deploymentId, "Download", $"Started url={assetUrl}", cancellationToken);
            var bytes = await _httpClient.GetByteArrayAsync(assetUrl, cancellationToken);
            await _fileSystem.WriteAllBytesAsync(zipPath, bytes, cancellationToken);
            await LogStageAsync(deploymentId, "Download", $"Completed bytes={bytes.Length}, path={zipPath}", cancellationToken);

            if (_fileSystem.DirectoryExists(stagingPath))
            {
                await LogStageAsync(deploymentId, "Staging", $"Deleting existing staging path={stagingPath}", cancellationToken);
                _fileSystem.DeleteDirectory(stagingPath, recursive: true);
            }

            _fileSystem.CreateDirectory(stagingPath);
            await LogStageAsync(deploymentId, "Extract", $"Started zip={zipPath}, destination={stagingPath}", cancellationToken);
            await _archiveService.ExtractZipAsync(zipPath, stagingPath, cancellationToken);
            await LogStageAsync(deploymentId, "Extract", $"Completed destination={stagingPath}", cancellationToken);

            await LogStageAsync(deploymentId, "ValidatePackage", $"Started path={stagingPath}", cancellationToken);
            var packageIsValid = await _deploymentPackageValidator.IsValidAsync(stagingPath, cancellationToken);
            await LogStageAsync(deploymentId, "ValidatePackage", $"Completed valid={packageIsValid}", cancellationToken);
            if (!packageIsValid)
            {
                return await FinalizeResultAsync(CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.ValidatingPackage,
                    "Deployment package validation failed.",
                    startedAtUtc,
                    DateTimeOffset.UtcNow), null, cancellationToken);
            }

            if (portOverride.HasValue)
            {
                await LogStageAsync(deploymentId, "PortOverride", $"Started port={portOverride.Value}, path={stagingPath}", cancellationToken);
                var overrideResult = await ApplyPortOverrideAsync(stagingPath, portOverride.Value, cancellationToken);
                await LogStageAsync(deploymentId, "PortOverride", overrideResult, cancellationToken);
                if (!string.Equals(overrideResult, "Completed", StringComparison.Ordinal))
                {
                    return await FinalizeResultAsync(CreateResult(
                        deploymentId,
                        appName,
                        version,
                        DeploymentStatus.Failed,
                        DeploymentStage.ValidatingPackage,
                        overrideResult,
                        startedAtUtc,
                        DateTimeOffset.UtcNow), null, cancellationToken);
                }
            }

            var rollbackAvailable = false;
            var serviceExists = await _serviceRegistrationService.ExistsAsync(appName, cancellationToken);
            await LogStageAsync(deploymentId, "ServiceRegistration", $"Exists check service={appName}, exists={serviceExists}", cancellationToken);
            if (serviceExists)
            {
                await LogStageAsync(deploymentId, "StopService", $"Requested service={appName}", cancellationToken);
                var stopResult = await _serviceControlService.StopAsync(appName, deploymentId, cancellationToken);
                await LogStageAsync(deploymentId, "StopService", DescribeCommandResult(stopResult), cancellationToken);
                if (!stopResult.Succeeded)
                {
                    return await FinalizeResultAsync(CreateResult(
                        deploymentId,
                        appName,
                        version,
                        DeploymentStatus.Failed,
                        DeploymentStage.StoppingService,
                        GetCommandMessage("Failed to stop service.", stopResult),
                        startedAtUtc,
                        DateTimeOffset.UtcNow), null, cancellationToken);
                }
            }
            else
            {
                await LogStageAsync(deploymentId, "StopService", $"Skipped because service={appName} is not installed yet", cancellationToken);
            }

            try
            {
                if (hadCurrent)
                {
                    if (_fileSystem.DirectoryExists(backupPath))
                    {
                        await LogStageAsync(deploymentId, "Backup", $"Deleting existing backup path={backupPath}", cancellationToken);
                        _fileSystem.DeleteDirectory(backupPath, recursive: true);
                    }

                    await LogStageAsync(deploymentId, "Backup", $"Moving current={currentPath} to backup={backupPath}", cancellationToken);
                    _fileSystem.MoveDirectory(currentPath, backupPath);
                    rollbackAvailable = true;
                    await LogStageAsync(deploymentId, "Backup", $"Completed backupPath={backupPath}", cancellationToken);
                }
                else
                {
                    await LogStageAsync(deploymentId, "Backup", "Skipped because no current deployment exists", cancellationToken);
                }

                if (_fileSystem.DirectoryExists(currentTempPath))
                {
                    await LogStageAsync(deploymentId, "Swap", $"Deleting existing current temp path={currentTempPath}", cancellationToken);
                    _fileSystem.DeleteDirectory(currentTempPath, recursive: true);
                }

                await LogStageAsync(deploymentId, "Swap", $"Moving staging={stagingPath} to currentTemp={currentTempPath}", cancellationToken);
                _fileSystem.MoveDirectory(stagingPath, currentTempPath);
                await LogStageAsync(deploymentId, "Swap", $"Moving currentTemp={currentTempPath} to current={currentPath}", cancellationToken);
                _fileSystem.MoveDirectory(currentTempPath, currentPath);
                await LogStageAsync(deploymentId, "Swap", $"Completed current={currentPath}", cancellationToken);
            }
            catch (Exception ex)
            {
                await LogStageAsync(deploymentId, "Swap", $"Failed error={ex.Message}", cancellationToken);
                if (rollbackAvailable)
                {
                    return await RollbackInternalAsync(
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

                return await FinalizeResultAsync(CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.ActivatingNewVersion,
                    ex.Message,
                    startedAtUtc,
                    DateTimeOffset.UtcNow), null, cancellationToken);
            }

            if (!serviceExists)
            {
                await LogStageAsync(deploymentId, "ServiceRegistration", $"Registering service={appName} from deploymentPath={currentPath}", cancellationToken);
                var registrationResult = await _serviceRegistrationService.RegisterAsync(appName, currentPath, cancellationToken);
                await LogStageAsync(deploymentId, "ServiceRegistration", DescribeCommandResult(registrationResult), cancellationToken);
                if (!registrationResult.Succeeded)
                {
                    if (rollbackAvailable)
                    {
                        return await RollbackInternalAsync(
                            deploymentId,
                            appName,
                            version,
                            startedAtUtc,
                            backupPath,
                            currentPath,
                            rollbackAvailable,
                            GetCommandMessage("Failed to register service.", registrationResult),
                            cancellationToken);
                    }

                    return await FinalizeResultAsync(CreateResult(
                        deploymentId,
                        appName,
                        version,
                        DeploymentStatus.Failed,
                        DeploymentStage.ActivatingNewVersion,
                        GetCommandMessage("Failed to register service.", registrationResult),
                        startedAtUtc,
                        DateTimeOffset.UtcNow), null, cancellationToken);
                }
            }

            await LogStageAsync(deploymentId, "Permissions", $"Ensuring runtime permissions service={appName}, path={currentPath}", cancellationToken);
            var permissionResult = await _servicePermissionService.EnsureRuntimePermissionsAsync(appName, currentPath, cancellationToken);
            await LogStageAsync(deploymentId, "Permissions", DescribeCommandResult(permissionResult), cancellationToken);
            if (!permissionResult.Succeeded)
            {
                if (rollbackAvailable)
                {
                    return await RollbackInternalAsync(
                        deploymentId,
                        appName,
                        version,
                        startedAtUtc,
                        backupPath,
                        currentPath,
                        rollbackAvailable,
                        GetCommandMessage("Failed to assign runtime permissions.", permissionResult),
                        cancellationToken);
                }

                return await FinalizeResultAsync(CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.ActivatingNewVersion,
                    GetCommandMessage("Failed to assign runtime permissions.", permissionResult),
                    startedAtUtc,
                    DateTimeOffset.UtcNow), null, cancellationToken);
            }

            await LogStageAsync(deploymentId, "StartService", $"Requested service={appName}", cancellationToken);
            var startResult = await _serviceControlService.StartAsync(appName, deploymentId, cancellationToken);
            await LogStageAsync(deploymentId, "StartService", DescribeCommandResult(startResult), cancellationToken);
            if (!startResult.Succeeded)
            {
                return await RollbackInternalAsync(
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

            await LogStageAsync(deploymentId, "Verify", $"Started service={appName}", cancellationToken);
            var deploymentVerified = await VerifyDeploymentWithRetryAsync(deploymentId, appName, cancellationToken);
            await LogStageAsync(deploymentId, "Verify", deploymentVerified ? "Completed" : "Failed after retry limit", cancellationToken);
            if (!deploymentVerified)
            {
                return await RollbackInternalAsync(
                    deploymentId,
                    appName,
                    version,
                    startedAtUtc,
                    backupPath,
                    currentPath,
                    rollbackAvailable,
                    "Deployment verification failed after deployment.",
                    cancellationToken);
            }

            CleanupOldBackups(appRoot);
            await LogStageAsync(deploymentId, "Cleanup", $"Completed appRoot={appRoot}", cancellationToken);

            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Succeeded,
                DeploymentStage.Completed,
                "Deployment completed successfully.",
                startedAtUtc,
                DateTimeOffset.UtcNow), rollbackAvailable ? backupPath : null, cancellationToken);
        }
        catch (Exception ex)
        {
            await LogStageAsync(deploymentId, "Deployment", $"Unhandled failure error={ex.Message}", cancellationToken);
            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                ex.Message,
                startedAtUtc,
                DateTimeOffset.UtcNow), hadCurrent ? backupPath : null, cancellationToken);
        }
    }

    private async Task<bool> VerifyDeploymentWithRetryAsync(string deploymentId, string appName, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var topology = await _appTopologyService.GetTopologyAsync(cancellationToken);
            var service = topology.FirstOrDefault(item =>
                string.Equals(item.ServiceName, appName, StringComparison.OrdinalIgnoreCase));
            var serviceVerified = service is not null &&
                service.Status == ServiceStatus.Running &&
                service.Ports.Count > 0;

            var healthVerified = serviceVerified && await _healthVerificationService.VerifyAsync(appName, cancellationToken);
            var ports = service is null || service.Ports.Count == 0
                ? "-"
                : string.Join(",", service.Ports);
            await LogStageAsync(
                deploymentId,
                "VerifyAttempt",
                $"attempt={attempt + 1}/10, serviceFound={service is not null}, status={service?.Status.ToString() ?? "Missing"}, ports={ports}, serviceVerified={serviceVerified}, healthVerified={healthVerified}",
                cancellationToken);
            if (healthVerified)
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

    private async Task<DeploymentResult> RollbackInternalAsync(
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
            await LogStageAsync(deploymentId, "Rollback", $"Skipped backupPath={backupPath}, hasBackup={hasBackup}", cancellationToken);
            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                $"{message} Rollback failed because no backup is available.",
                startedAtUtc,
                DateTimeOffset.UtcNow), null, cancellationToken);
        }

        try
        {
            await LogStageAsync(deploymentId, "Rollback", $"Started reason={message}", cancellationToken);
            await LogStageAsync(deploymentId, "Rollback", $"Stopping service={appName}", cancellationToken);
            var stopResult = await _serviceControlService.StopAsync(appName, deploymentId, cancellationToken);
            await LogStageAsync(deploymentId, "Rollback", $"Stop result {DescribeCommandResult(stopResult)}", cancellationToken);
            if (!stopResult.Succeeded)
            {
                return await FinalizeResultAsync(CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.Failed,
                    $"{message} Rollback stop failed. {GetCommandMessage("Service stop failed.", stopResult)}",
                    startedAtUtc,
                    DateTimeOffset.UtcNow), backupPath, cancellationToken);
            }

            if (_fileSystem.DirectoryExists(currentPath))
            {
                await LogStageAsync(deploymentId, "Rollback", $"Deleting current path={currentPath}", cancellationToken);
                _fileSystem.DeleteDirectory(currentPath, recursive: true);
            }

            await LogStageAsync(deploymentId, "Rollback", $"Restoring backup={backupPath} to current={currentPath}", cancellationToken);
            _fileSystem.MoveDirectory(backupPath, currentPath);
            await LogStageAsync(deploymentId, "Rollback", "RestoreBackup completed", cancellationToken);

            await LogStageAsync(deploymentId, "Rollback", $"Restarting service={appName}", cancellationToken);
            var restartResult = await _serviceControlService.StartAsync(appName, deploymentId, cancellationToken);
            await LogStageAsync(deploymentId, "Rollback", $"Restart result {DescribeCommandResult(restartResult)}", cancellationToken);
            if (!restartResult.Succeeded)
            {
                return await FinalizeResultAsync(CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.Failed,
                    $"{message} Rollback restart failed. {GetCommandMessage("Service start failed.", restartResult)}",
                    startedAtUtc,
                    DateTimeOffset.UtcNow), backupPath, cancellationToken);
            }

            var deploymentVerified = await VerifyDeploymentWithRetryAsync(deploymentId, appName, cancellationToken);
            await LogStageAsync(deploymentId, "Rollback", deploymentVerified ? "Verification succeeded" : "Verification failed", cancellationToken);
            if (!deploymentVerified)
            {
                return await FinalizeResultAsync(CreateResult(
                    deploymentId,
                    appName,
                    version,
                    DeploymentStatus.Failed,
                    DeploymentStage.Failed,
                    "Rollback failed - manual intervention required",
                    startedAtUtc,
                    DateTimeOffset.UtcNow), backupPath, cancellationToken);
            }

            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.RolledBack,
                DeploymentStage.RolledBack,
                $"{message} Rollback completed successfully.",
                startedAtUtc,
                DateTimeOffset.UtcNow), backupPath, cancellationToken);
        }
        catch (Exception ex)
        {
            await LogStageAsync(deploymentId, "Rollback", $"Failed error={ex.Message}", cancellationToken);
            return await FinalizeResultAsync(CreateResult(
                deploymentId,
                appName,
                version,
                DeploymentStatus.Failed,
                DeploymentStage.Failed,
                $"{message} Rollback failed: {ex.Message}",
                startedAtUtc,
                DateTimeOffset.UtcNow), backupPath, cancellationToken);
        }
    }

    private void CleanupOldBackups(string appRoot)
    {
        var backupDirectories = _fileSystem.GetDirectories(appRoot)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(name)
                    && name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Skip(5)
            .ToList();

        foreach (var backupDirectory in backupDirectories)
        {
            _fileSystem.DeleteDirectory(backupDirectory, recursive: true);
        }
    }

    private async Task<DeploymentResult> FinalizeResultAsync(
        DeploymentResult result,
        string? backupPath,
        CancellationToken cancellationToken)
    {
        await LogStageAsync(
            result.DeploymentId,
            "Result",
            $"status={result.Status}, stage={result.Stage}, message={result.Message ?? "-"}, backupPath={backupPath ?? "-"}",
            cancellationToken);
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
            BackupPath = string.IsNullOrWhiteSpace(backupPath) ? null : backupPath
        }, cancellationToken);

        return result;
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
            OperationId = deploymentId,
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

    private async Task LogStageAsync(string deploymentId, string stage, string message, CancellationToken cancellationToken)
    {
        await _operationLogger.LogAsync(deploymentId, stage, message, cancellationToken);
    }

    private static string DescribeCommandResult(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        var normalizedDetails = string.IsNullOrWhiteSpace(details)
            ? "-"
            : details.ReplaceLineEndings(" ").Trim();

        return $"exitCode={result.ExitCode}, succeeded={result.Succeeded}, details={normalizedDetails}";
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

    private async Task<string> ApplyPortOverrideAsync(string stagingPath, int portOverride, CancellationToken cancellationToken)
    {
        var appSettingsPath = _fileSystem.GetFiles(stagingPath, "appsettings.json", recursive: true)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(appSettingsPath) || !_fileSystem.FileExists(appSettingsPath))
        {
            return "Port override requested but appsettings.json was not found.";
        }

        var content = await _fileSystem.ReadAllTextAsync(appSettingsPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Port override requested but appsettings.json is empty.";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!TryRewriteKestrelHttpUrl(document.RootElement, portOverride, out var rewrittenJson))
            {
                return "Port override requested but Kestrel:Endpoints:Http:Url was not found in appsettings.json.";
            }

            await _fileSystem.WriteAllBytesAsync(appSettingsPath, Encoding.UTF8.GetBytes(rewrittenJson), cancellationToken);
            return "Completed";
        }
        catch (JsonException)
        {
            return "Port override requested but appsettings.json is not valid JSON.";
        }
    }

    private static bool TryRewriteKestrelHttpUrl(JsonElement root, int portOverride, out string rewrittenJson)
    {
        rewrittenJson = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty("Kestrel", out var kestrel))
        {
            using var defaultStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(defaultStream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    property.WriteTo(writer);
                }

                WriteDefaultKestrel(writer, portOverride);
                writer.WriteEndObject();
            }

            rewrittenJson = Encoding.UTF8.GetString(defaultStream.ToArray());
            return true;
        }

        if (kestrel.ValueKind != JsonValueKind.Object ||
            !kestrel.TryGetProperty("Endpoints", out var endpoints) ||
            endpoints.ValueKind != JsonValueKind.Object ||
            !endpoints.TryGetProperty("Http", out var http) ||
            http.ValueKind != JsonValueKind.Object ||
            !http.TryGetProperty("Url", out var url) ||
            url.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, "Kestrel", StringComparison.Ordinal))
                {
                    property.WriteTo(writer);
                    continue;
                }

                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                foreach (var kestrelProperty in kestrel.EnumerateObject())
                {
                    if (!string.Equals(kestrelProperty.Name, "Endpoints", StringComparison.Ordinal))
                    {
                        kestrelProperty.WriteTo(writer);
                        continue;
                    }

                    writer.WritePropertyName(kestrelProperty.Name);
                    writer.WriteStartObject();
                    foreach (var endpointProperty in endpoints.EnumerateObject())
                    {
                        if (!string.Equals(endpointProperty.Name, "Http", StringComparison.Ordinal))
                        {
                            endpointProperty.WriteTo(writer);
                            continue;
                        }

                        writer.WritePropertyName(endpointProperty.Name);
                        writer.WriteStartObject();
                        foreach (var httpProperty in http.EnumerateObject())
                        {
                            if (string.Equals(httpProperty.Name, "Url", StringComparison.Ordinal))
                            {
                                writer.WriteString(httpProperty.Name, $"http://*:{portOverride}");
                            }
                            else
                            {
                                httpProperty.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        rewrittenJson = Encoding.UTF8.GetString(stream.ToArray());
        return true;
    }

    private static void WriteDefaultKestrel(Utf8JsonWriter writer, int portOverride)
    {
        writer.WritePropertyName("Kestrel");
        writer.WriteStartObject();
        writer.WritePropertyName("Endpoints");
        writer.WriteStartObject();
        writer.WritePropertyName("Http");
        writer.WriteStartObject();
        writer.WriteString("Url", $"http://0.0.0.0:{portOverride}");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("Limits");
        writer.WriteStartObject();
        writer.WriteNumber("MaxRequestBodySize", 52428800);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
