using System.Text;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.Host;

public sealed class ServiceRegistrationService : IServiceRegistrationService
{
    private readonly ICommandRunner _commandRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly IOptions<ServiceRegistrationOptions> _serviceRegistrationOptions;

    public ServiceRegistrationService(
        ICommandRunner commandRunner,
        IFileSystem fileSystem,
        IRuntimeEnvironment runtimeEnvironment,
        IOptions<ServiceRegistrationOptions> serviceRegistrationOptions)
    {
        _commandRunner = commandRunner;
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
        _serviceRegistrationOptions = serviceRegistrationOptions;
    }

    public async Task<bool> ExistsAsync(string serviceName, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        var result = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["query", validatedName]
            }, ct)
            : await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "systemctl",
                Arguments = ["show", validatedName, "--property", "LoadState"]
            }, ct);

        if (_runtimeEnvironment.GetCurrentOs() == OsType.Windows)
        {
            return result.Succeeded;
        }

        return result.Succeeded &&
            result.StdOut.Contains("LoadState=loaded", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CommandResult> RegisterAsync(string serviceName, string deploymentPath, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        if (string.IsNullOrWhiteSpace(deploymentPath))
        {
            throw new ArgumentException("Deployment path is required.", nameof(deploymentPath));
        }

        return _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await RegisterWindowsAsync(validatedName, deploymentPath, ct)
            : await RegisterLinuxAsync(validatedName, deploymentPath, ct);
    }

    public async Task<CommandResult> UnregisterAsync(string serviceName, CancellationToken ct = default)
    {
        var validatedName = ValidateServiceName(serviceName);
        return _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest
            {
                Command = "sc",
                Arguments = ["delete", validatedName]
            }, ct)
            : await UnregisterLinuxAsync(validatedName, ct);
    }

    private async Task<CommandResult> RegisterWindowsAsync(string serviceName, string deploymentPath, CancellationToken ct)
    {
        var executablePath = _fileSystem.GetFiles(deploymentPath, "*.exe", recursive: true)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = $"No executable was found in '{deploymentPath}'."
            };
        }

        return await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "sc",
            Arguments = ["create", serviceName, $"binPath={Quote(executablePath)}", "start=auto", "obj=LocalSystem"]
        }, ct);
    }

    private async Task<CommandResult> RegisterLinuxAsync(string serviceName, string deploymentPath, CancellationToken ct)
    {
        var appUser = _serviceRegistrationOptions.Value.LinuxAppUser?.Trim();
        if (string.IsNullOrWhiteSpace(appUser))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Linux app user is required."
            };
        }

        var dllPath = _fileSystem.GetFiles(deploymentPath, "*.dll", recursive: true)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(dllPath))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = $"No runnable .dll was found in '{deploymentPath}'."
            };
        }

        var unitPath = _fileSystem.Combine(_runtimeEnvironment.GetSystemdServiceDirectory(), $"{serviceName}.service");
        var unitContents = BuildLinuxUnitFile(serviceName, deploymentPath, dllPath, appUser);
        await _fileSystem.WriteAllBytesAsync(unitPath, Encoding.UTF8.GetBytes(unitContents), ct);

        var reloadResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["daemon-reload"]
        }, ct);

        if (!reloadResult.Succeeded)
        {
            return reloadResult;
        }

        var enableResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["enable", serviceName]
        }, ct);

        if (!enableResult.Succeeded)
        {
            return enableResult;
        }

        return new CommandResult
        {
            ExitCode = 0,
            StdOut = $"linuxUser={appUser}; deploymentPath={deploymentPath}; dll={dllPath}; unitPath={unitPath}"
        };
    }

    private static string BuildLinuxUnitFile(string serviceName, string deploymentPath, string dllPath, string appUser)
    {
        return $$"""
[Unit]
Description={{serviceName}}
After=network.target

[Service]
User={{appUser}}
Group={{appUser}}
WorkingDirectory={{deploymentPath}}
ExecStart=/usr/bin/dotnet {{dllPath}}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
""";
    }

    private async Task<CommandResult> UnregisterLinuxAsync(string serviceName, CancellationToken ct)
    {
        var disableResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["disable", serviceName]
        }, ct);

        if (!disableResult.Succeeded)
        {
            return disableResult;
        }

        var unitPath = _fileSystem.Combine(_runtimeEnvironment.GetSystemdServiceDirectory(), $"{serviceName}.service");
        if (_fileSystem.FileExists(unitPath))
        {
            _fileSystem.DeleteFile(unitPath);
        }

        var reloadResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["daemon-reload"]
        }, ct);

        if (!reloadResult.Succeeded)
        {
            return reloadResult;
        }

        return new CommandResult
        {
            ExitCode = 0,
            StdOut = $"unitPath={unitPath}"
        };
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string ValidateServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name is required.", nameof(serviceName));
        }

        return serviceName.Trim();
    }
}
