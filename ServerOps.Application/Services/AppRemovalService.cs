using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Application.Services;

public sealed class AppRemovalService : IAppRemovalService
{
    private readonly IExposureService _exposureService;
    private readonly IServiceControlService _serviceControlService;
    private readonly IServiceRegistrationService _serviceRegistrationService;
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IOperationLogger _operationLogger;

    public AppRemovalService(
        IExposureService exposureService,
        IServiceControlService serviceControlService,
        IServiceRegistrationService serviceRegistrationService,
        IFileSystem fileSystem,
        IRuntimeEnvironment runtimeEnvironment,
        IOperationLogger operationLogger)
    {
        _exposureService = exposureService;
        _serviceControlService = serviceControlService;
        _serviceRegistrationService = serviceRegistrationService;
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
        _operationLogger = operationLogger;
    }

    public async Task<CommandResult> RemoveAsync(string appName, string? operationId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return new CommandResult
            {
                OperationId = operationId ?? string.Empty,
                ExitCode = -1,
                StdErr = "Application name is required."
            };
        }

        var normalizedAppName = appName.Trim();
        operationId = string.IsNullOrWhiteSpace(operationId)
            ? Guid.NewGuid().ToString("N")
            : operationId.Trim();
        var messages = new List<string>();

        try
        {
            await _operationLogger.LogAsync(operationId, "Remove", $"Started app={normalizedAppName}", ct);

            await _exposureService.UnexposeAsync(normalizedAppName, operationId, ct);
            messages.Add("Exposure removed");
            await _operationLogger.LogAsync(operationId, "Remove", "Exposure removed", ct);

            var serviceExists = await _serviceRegistrationService.ExistsAsync(normalizedAppName, ct);
            await _operationLogger.LogAsync(operationId, "Remove", $"Service exists={serviceExists}", ct);

            if (serviceExists)
            {
                var stopResult = await _serviceControlService.StopAsync(normalizedAppName, operationId, ct);
                await _operationLogger.LogAsync(operationId, "Remove", $"Stop {Describe(stopResult)}", ct);
                if (!stopResult.Succeeded && !IsAlreadyStopped(stopResult))
                {
                    return new CommandResult
                    {
                        OperationId = operationId,
                        ExitCode = stopResult.ExitCode == 0 ? -1 : stopResult.ExitCode,
                        StdErr = GetMessage("Failed to stop service.", stopResult)
                    };
                }

                messages.Add(stopResult.Succeeded ? "Service stopped" : "Service already stopped");

                var unregisterResult = await _serviceRegistrationService.UnregisterAsync(normalizedAppName, ct);
                await _operationLogger.LogAsync(operationId, "Remove", $"Unregister {Describe(unregisterResult)}", ct);
                if (!unregisterResult.Succeeded)
                {
                    return new CommandResult
                    {
                        OperationId = operationId,
                        ExitCode = unregisterResult.ExitCode == 0 ? -1 : unregisterResult.ExitCode,
                        StdErr = GetMessage("Failed to unregister service.", unregisterResult)
                    };
                }

                messages.Add("Service unregistered");
            }
            else
            {
                messages.Add("Service not installed");
            }

            var appRoot = _fileSystem.Combine(_runtimeEnvironment.GetAppsRootPath(), normalizedAppName);
            if (_fileSystem.DirectoryExists(appRoot))
            {
                _fileSystem.DeleteDirectory(appRoot, recursive: true);
                messages.Add("App files removed");
                await _operationLogger.LogAsync(operationId, "Remove", $"Deleted appRoot={appRoot}", ct);
            }
            else
            {
                messages.Add("App files already removed");
                await _operationLogger.LogAsync(operationId, "Remove", $"App root missing path={appRoot}", ct);
            }

            await _operationLogger.LogAsync(operationId, "Remove", "Completed", ct);
            return new CommandResult
            {
                OperationId = operationId,
                ExitCode = 0,
                StdOut = string.Join(". ", messages)
            };
        }
        catch (Exception ex)
        {
            await _operationLogger.LogAsync(operationId, "Remove", $"Failed error={ex.Message}", ct);
            return new CommandResult
            {
                OperationId = operationId,
                ExitCode = -1,
                StdErr = ex.Message
            };
        }
    }

    private static string Describe(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details)
            ? $"exitCode={result.ExitCode}, succeeded={result.Succeeded}"
            : $"exitCode={result.ExitCode}, succeeded={result.Succeeded}, details={details.ReplaceLineEndings(" ").Trim()}";
    }

    private static string GetMessage(string prefix, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details.Trim()}";
    }

    private static bool IsAlreadyStopped(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        if (string.IsNullOrWhiteSpace(details))
        {
            return false;
        }

        return details.Contains("FAILED 1062", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("has not been started", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("not running", StringComparison.OrdinalIgnoreCase);
    }
}
