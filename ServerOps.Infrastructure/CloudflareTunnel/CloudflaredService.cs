using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.CloudflareTunnel;

public sealed class CloudflaredService : ICloudflaredService
{
    private readonly ICommandRunner _commandRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public CloudflaredService(
        ICommandRunner commandRunner,
        IFileSystem fileSystem,
        IRuntimeEnvironment runtimeEnvironment)
    {
        _commandRunner = commandRunner;
        _fileSystem = fileSystem;
        _runtimeEnvironment = runtimeEnvironment;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "cloudflared",
            Arguments = ["--version"]
        }, cancellationToken);

        return result.Succeeded;
    }

    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    {
        var result = _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest { Command = "sc", Arguments = ["query", "cloudflared"] }, cancellationToken)
            : await _commandRunner.RunAsync(new CommandRequest { Command = "systemctl", Arguments = ["status", "cloudflared", "--no-pager"] }, cancellationToken);

        return result.StdOut.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
               result.StdOut.Contains("active (running)", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<TunnelInfo> GetTunnelInfoAsync(CancellationToken cancellationToken = default)
    {
        var configPath = _runtimeEnvironment.GetCloudflaredConfigPath();
        var isRunning = await IsRunningAsync(cancellationToken);

        if (!_fileSystem.FileExists(configPath))
        {
            return new TunnelInfo
            {
                ConfigPath = configPath,
                IsRunning = isRunning
            };
        }

        var contents = await _fileSystem.ReadAllTextAsync(configPath, cancellationToken);
        var insideIngress = false;
        var tunnelId = string.Empty;
        var ingressRules = new List<string>();

        foreach (var rawLine in contents.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("tunnel:", StringComparison.OrdinalIgnoreCase))
            {
                tunnelId = line["tunnel:".Length..].Trim();
            }
            else if (line.StartsWith("ingress:", StringComparison.OrdinalIgnoreCase))
            {
                insideIngress = true;
            }
            else if (insideIngress && line.StartsWith("- hostname:", StringComparison.OrdinalIgnoreCase))
            {
                ingressRules.Add(line["- hostname:".Length..].Trim());
            }
            else if (insideIngress && line.StartsWith("service:", StringComparison.OrdinalIgnoreCase))
            {
                ingressRules.Add(line["service:".Length..].Trim());
            }
        }

        return new TunnelInfo
        {
            TunnelId = tunnelId,
            IsRunning = isRunning,
            ConfigPath = configPath,
            IngressRules = ingressRules
        };
    }

    public async Task<CommandResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        var installResult = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "cloudflared",
            Arguments = ["service", "install"]
        }, cancellationToken);

        if (!installResult.Succeeded)
        {
            return installResult;
        }

        return _runtimeEnvironment.GetCurrentOs() == OsType.Windows
            ? await _commandRunner.RunAsync(new CommandRequest { Command = "sc", Arguments = ["start", "cloudflared"] }, cancellationToken)
            : await _commandRunner.RunAsync(new CommandRequest { Command = "systemctl", Arguments = ["start", "cloudflared"] }, cancellationToken);
    }

    public async Task<CommandResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeEnvironment.GetCurrentOs() != OsType.Windows)
        {
            return await _commandRunner.RunAsync(
                new CommandRequest { Command = "systemctl", Arguments = ["restart", "cloudflared"] },
                cancellationToken);
        }

        var stopResult = await _commandRunner.RunAsync(
            new CommandRequest { Command = "sc", Arguments = ["stop", "cloudflared"] },
            cancellationToken);

        if (!stopResult.Succeeded)
        {
            return stopResult;
        }

        return await _commandRunner.RunAsync(
            new CommandRequest { Command = "sc", Arguments = ["start", "cloudflared"] },
            cancellationToken);
    }
}
