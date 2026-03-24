using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.Host;

public sealed class ServicePermissionService : IServicePermissionService
{
    private readonly ICommandRunner _commandRunner;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IOptions<ServiceRegistrationOptions> _serviceRegistrationOptions;

    public ServicePermissionService(
        ICommandRunner commandRunner,
        IRuntimeEnvironment runtimeEnvironment,
        IOptions<ServiceRegistrationOptions> serviceRegistrationOptions)
    {
        _commandRunner = commandRunner;
        _runtimeEnvironment = runtimeEnvironment;
        _serviceRegistrationOptions = serviceRegistrationOptions;
    }

    public async Task<CommandResult> EnsureRuntimePermissionsAsync(string serviceName, string deploymentPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name is required.", nameof(serviceName));
        }

        if (string.IsNullOrWhiteSpace(deploymentPath))
        {
            throw new ArgumentException("Deployment path is required.", nameof(deploymentPath));
        }

        if (_runtimeEnvironment.GetCurrentOs() == OsType.Windows)
        {
            return new CommandResult
            {
                ExitCode = 0,
                StdOut = $"windowsServiceAccount=LocalSystem; deploymentPath={deploymentPath}"
            };
        }

        var appUser = _serviceRegistrationOptions.Value.LinuxAppUser?.Trim();
        if (string.IsNullOrWhiteSpace(appUser))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Linux app user is required."
            };
        }

        var ensureUserResult = await EnsureLinuxUserAsync(appUser, ct);
        if (!ensureUserResult.Succeeded)
        {
            return ensureUserResult;
        }

        var chownResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "chown",
            Arguments = ["-R", $"{appUser}:{appUser}", deploymentPath]
        }, ct);

        if (!chownResult.Succeeded)
        {
            return chownResult;
        }

        return new CommandResult
        {
            ExitCode = 0,
            StdOut = $"linuxUser={appUser}; deploymentPath={deploymentPath}"
        };
    }

    private async Task<CommandResult> EnsureLinuxUserAsync(string appUser, CancellationToken ct)
    {
        var existsResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "id",
            Arguments = ["-u", appUser]
        }, ct);

        if (existsResult.Succeeded)
        {
            return new CommandResult
            {
                ExitCode = 0,
                StdOut = $"linux user '{appUser}' already exists"
            };
        }

        var createResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "useradd",
            Arguments = ["--system", "--create-home", "--user-group", "--shell", "/usr/sbin/nologin", appUser]
        }, ct);

        if (!createResult.Succeeded)
        {
            return createResult;
        }

        return new CommandResult
        {
            ExitCode = 0,
            StdOut = $"linux user '{appUser}' created"
        };
    }
}
