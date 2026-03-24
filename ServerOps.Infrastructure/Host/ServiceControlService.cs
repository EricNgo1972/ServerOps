using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host;

public sealed class ServiceControlService : IServiceControlService
{
    private const int WindowsStopPollDelayMilliseconds = 500;
    private const int WindowsStopPollAttempts = 10;

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

        var result = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["start", validatedName]
            }, ct)
            : await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["start", validatedName]
            }, ct);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Start {DescribeCommandResult(result)}", ct);
        return WrapResult(result, resolvedOperationId);
    }

    public async Task<CommandResult> StopAsync(string serviceName, string? operationId = null, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        var resolvedOperationId = ResolveOperationId(operationId);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Stop requested service={validatedName}", ct);

        var result = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["stop", validatedName]
            }, ct)
            : await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["stop", validatedName]
            }, ct);
        await _operationLogger.LogAsync(resolvedOperationId, "ServiceControl", $"Stop {DescribeCommandResult(result)}", ct);
        return WrapResult(result, resolvedOperationId);
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

        if (!stopResult.Succeeded)
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
        Stopped = 2,
        Failed = 3
    }
}
