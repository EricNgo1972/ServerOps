using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host;

public sealed class ServiceControlService : IServiceControlService
{
    private const int WindowsStopPollDelayMilliseconds = 200;
    private const int WindowsStopPollAttempts = 20;

    private readonly ICommandRunner _commandRunner;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public ServiceControlService(ICommandRunner commandRunner, IRuntimeEnvironment runtimeEnvironment)
    {
        _commandRunner = commandRunner;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public Task<CommandResult> StartAsync(string serviceName, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);

        return _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["start", validatedName],
                Allowed = true
            }, ct)
            : _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["start", validatedName],
                Allowed = true
            }, ct);
    }

    public Task<CommandResult> StopAsync(string serviceName, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);

        return _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["stop", validatedName],
                Allowed = true
            }, ct)
            : _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["stop", validatedName],
                Allowed = true
            }, ct);
    }

    public async Task<CommandResult> RestartAsync(string serviceName, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);

        if (_runtimeEnvironment.GetCurrentOs() != OsType.Windows)
        {
            return await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["restart", validatedName],
                Allowed = true
            }, ct);
        }

        var stopResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["stop", validatedName],
            Allowed = true
        }, ct);

        if (!stopResult.Succeeded)
        {
            return stopResult;
        }

        var waitResult = await WaitForWindowsServiceStoppedAsync(validatedName, ct);
        if (!waitResult.Succeeded)
        {
            return waitResult;
        }

        return await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["start", validatedName],
            Allowed = true
        }, ct);
    }

    private async Task<CommandResult> WaitForWindowsServiceStoppedAsync(string serviceName, CancellationToken ct)
    {
        for (var attempt = 0; attempt < WindowsStopPollAttempts; attempt++)
        {
            var queryResult = await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["query", serviceName],
                Allowed = true
            }, ct);

            if (!queryResult.Succeeded)
            {
                return queryResult;
            }

            var state = ParseWindowsState(queryResult.StdOut);
            if (state == WindowsServiceQueryState.Stopped)
            {
                return queryResult;
            }

            if (state == WindowsServiceQueryState.Failed)
            {
                return new CommandResult
                {
                    ExitCode = 1,
                    StdOut = queryResult.StdOut,
                    StdErr = string.IsNullOrWhiteSpace(queryResult.StdErr)
                        ? "Service entered a failed state while stopping."
                        : queryResult.StdErr
                };
            }

            if (attempt < WindowsStopPollAttempts - 1)
            {
                await Task.Delay(WindowsStopPollDelayMilliseconds, ct);
            }
        }

        return new CommandResult
        {
            ExitCode = 1,
            StdErr = $"Timed out waiting for service '{serviceName}' to stop."
        };
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
