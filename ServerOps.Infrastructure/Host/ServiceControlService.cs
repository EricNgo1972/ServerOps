using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host;

public sealed class ServiceControlService : IServiceControlService
{
    private const int WindowsStopPollDelayMilliseconds = 500;
    private const int WindowsStopPollAttempts = 10;
    private const int WindowsStartPollDelayMilliseconds = 1000;
    private const int WindowsStartPollAttempts = 30;

    private readonly ICommandRunner _commandRunner;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IOperationLogger _operationLogger;

    public ServiceControlService(ICommandRunner commandRunner, IRuntimeEnvironment runtimeEnvironment, IOperationLogger operationLogger)
    {
        _commandRunner = commandRunner;
        _runtimeEnvironment = runtimeEnvironment;
        _operationLogger = operationLogger;
    }

    public async Task<CommandResult> StartAsync(string serviceName, string? operationId = null, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        var resolvedOperationId = ResolveOperationId(operationId);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Start requested service={validatedName}", ct);

        if (_runtimeEnvironment.GetCurrentOs() != OsType.Windows)
        {
            var linuxResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["start", validatedName]
            }, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Start {DescribeCommandResult(linuxResult)}", ct);
            return WrapResult(linuxResult, resolvedOperationId);
        }

        var startResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["start", validatedName]
        }, ct);

        if (!startResult.Succeeded)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Start {DescribeCommandResult(startResult)}", ct);
            return WrapResult(startResult, resolvedOperationId);
        }

        var running = await WaitForRunning(validatedName, ct);
        if (running)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Start {DescribeCommandResult(startResult)}", ct);
            return WrapResult(startResult, resolvedOperationId);
        }

        var finalQuery = await QueryWindowsServiceAsync(validatedName, ct);
        var failedResult = new CommandResult
        {
            ExitCode = 1053,
            StdErr = $"Service did not reach RUNNING state after start. {GetQueryDetails(finalQuery)}"
        };
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Start {DescribeCommandResult(failedResult)}", ct);
        return WrapResult(failedResult, resolvedOperationId);
    }

    public async Task<CommandResult> StopAsync(string serviceName, string? operationId = null, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        var resolvedOperationId = ResolveOperationId(operationId);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Stop requested service={validatedName}", ct);

        if (_runtimeEnvironment.GetCurrentOs() != OsType.Windows)
        {
            var linuxResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["stop", validatedName]
            }, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Stop {DescribeCommandResult(linuxResult)}", ct);
            return WrapResult(linuxResult, resolvedOperationId);
        }

        var stopResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["stop", validatedName]
        }, ct);

        if (stopResult.Succeeded || IsAlreadyStopped(stopResult))
        {
            var normalizedResult = stopResult.Succeeded
                ? stopResult
                : new CommandResult
                {
                    ExitCode = 0,
                    StdOut = string.IsNullOrWhiteSpace(stopResult.StdOut)
                        ? "Service is already stopped."
                        : $"{stopResult.StdOut}{Environment.NewLine}Service is already stopped."
                };

            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Stop {DescribeCommandResult(normalizedResult)}", ct);
            return WrapResult(normalizedResult, resolvedOperationId);
        }

        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Stop {DescribeCommandResult(stopResult)}", ct);
        return WrapResult(stopResult, resolvedOperationId);
    }

    public async Task<CommandResult> RestartAsync(string serviceName, string? operationId = null, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        var resolvedOperationId = ResolveOperationId(operationId);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Restart requested service={validatedName}", ct);

        if (_runtimeEnvironment.GetCurrentOs() != OsType.Windows)
        {
            var linuxResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["restart", validatedName]
            }, ct);
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Restart {DescribeCommandResult(linuxResult)}", ct);
            return WrapResult(linuxResult, resolvedOperationId);
        }

        var stopResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["stop", validatedName]
        }, ct);

        if (!stopResult.Succeeded && !IsAlreadyStopped(stopResult))
        {
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Restart stop {DescribeCommandResult(stopResult)}", ct);
            return WrapResult(stopResult, resolvedOperationId);
        }

        var stopped = await WaitForStopped(validatedName, ct);
        if (!stopped)
        {
            await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", "Restart waitForStop timed_out", ct);
            return WrapResult(stopResult, resolvedOperationId);
        }

        var startResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["start", validatedName]
        }, ct);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Restart start {DescribeCommandResult(startResult)}", ct);
        return WrapResult(startResult, resolvedOperationId);
    }

    private static string ResolveOperationId(string? operationId) =>
        string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();

    private static CommandResult WrapResult(CommandResult result, string operationId) =>
        new()
        {
            OperationId = operationId,
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr
        };

    private static string DescribeCommandResult(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(details)
            ? $"exitCode={result.ExitCode}, succeeded={result.Succeeded}"
            : $"exitCode={result.ExitCode}, succeeded={result.Succeeded}, details={details.ReplaceLineEndings(" ").Trim()}";
    }

    private async Task<bool> WaitForStopped(string serviceName, CancellationToken ct)
    {
        for (var attempt = 0; attempt < WindowsStopPollAttempts; attempt++)
        {
            var queryResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["query", serviceName]
            }, ct);

            if (!queryResult.Succeeded)
            {
                return false;
            }

            var state = ParseWindowsState(queryResult.StdOut);
            if (state == WindowsServiceQueryState.Stopped)
            {
                return true;
            }

            if (state == WindowsServiceQueryState.Failed)
            {
                return false;
            }

            if (attempt < WindowsStopPollAttempts - 1)
            {
                await Task.Delay(WindowsStopPollDelayMilliseconds, ct);
            }
        }

        return false;
    }

    private async Task<bool> WaitForRunning(string serviceName, CancellationToken ct)
    {
        for (var attempt = 0; attempt < WindowsStartPollAttempts; attempt++)
        {
            var queryResult = await QueryWindowsServiceAsync(serviceName, ct);
            if (!queryResult.Succeeded)
            {
                return false;
            }

            var state = ParseWindowsState(queryResult.StdOut);
            if (state == WindowsServiceQueryState.Running)
            {
                return true;
            }

            if (state == WindowsServiceQueryState.Stopped || state == WindowsServiceQueryState.Failed)
            {
                return false;
            }

            if (attempt < WindowsStartPollAttempts - 1)
            {
                await Task.Delay(WindowsStartPollDelayMilliseconds, ct);
            }
        }

        return false;
    }

    private async Task<CommandResult> QueryWindowsServiceAsync(string serviceName, CancellationToken ct)
    {
        return await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["query", serviceName]
        }, ct);
    }

    private static WindowsServiceQueryState ParseWindowsState(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return WindowsServiceQueryState.Unknown;
        }

        var normalized = output.ToUpperInvariant();
        if (normalized.Contains("STOPPED", StringComparison.Ordinal))
        {
            return WindowsServiceQueryState.Stopped;
        }

        if (normalized.Contains("STOP_PENDING", StringComparison.Ordinal))
        {
            return WindowsServiceQueryState.StopPending;
        }

        if (normalized.Contains("RUNNING", StringComparison.Ordinal))
        {
            return WindowsServiceQueryState.Running;
        }

        if (normalized.Contains("START_PENDING", StringComparison.Ordinal))
        {
            return WindowsServiceQueryState.StartPending;
        }

        if (normalized.Contains("FAILED", StringComparison.Ordinal) ||
            normalized.Contains("FAILURE", StringComparison.Ordinal))
        {
            return WindowsServiceQueryState.Failed;
        }

        return WindowsServiceQueryState.Unknown;
    }

    private static string ValidateServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name is required.", nameof(serviceName));
        }

        return serviceName.Trim();
    }

    private enum WindowsServiceQueryState
    {
        Unknown = 0,
        StopPending = 1,
        StartPending = 2,
        Running = 3,
        Stopped = 4,
        Failed = 5
    }

    private static bool IsAlreadyStopped(CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return details.Contains("FAILED 1062", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("has not been started", StringComparison.OrdinalIgnoreCase) ||
               details.Contains("not been started", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetQueryDetails(CommandResult queryResult)
    {
        var details = string.IsNullOrWhiteSpace(queryResult.StdErr) ? queryResult.StdOut : queryResult.StdErr;
        return string.IsNullOrWhiteSpace(details) ? "Unable to query service state." : details.ReplaceLineEndings(" ").Trim();
    }
}
