using System.Diagnostics;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;

namespace ServerOps.Infrastructure.Host;

public sealed class CommandRunner : ICommandRunner
{
    private static readonly HashSet<string> LinuxCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "systemctl",
        "ss",
        "cloudflared",
        "apt-get",
        "chmod",
        "id",
        "useradd",
        "chown"
    };

    private static readonly HashSet<string> WindowsCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "sc",
        "net",
        "netstat",
        "cloudflared",
        "winget",
        "powershell"
    };

    private readonly IRuntimeEnvironment _runtimeEnvironment;

    public CommandRunner(IRuntimeEnvironment runtimeEnvironment)
    {
        _runtimeEnvironment = runtimeEnvironment;
    }

    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Command is required."
            };
        }

        var command = request.Command.Trim();
        if (!IsAllowedCommand(command))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = $"Command '{command}' is not in the whitelist."
            };
        }

        foreach (var argument in request.Arguments)
        {
            if (argument.Contains('\n') || argument.Contains('\r') || argument.Contains('\0'))
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = "Arguments contain invalid control characters."
                };
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = await stdOutTask,
            StdErr = await stdErrTask
        };
    }

    private bool IsAllowedCommand(string command)
    {
        var normalizedCommand = command;
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
        {
            normalizedCommand = Path.GetFileNameWithoutExtension(command);
        }

        return _runtimeEnvironment.GetCurrentOs() switch
        {
            OsType.Linux => LinuxCommands.Contains(normalizedCommand),
            OsType.Windows => WindowsCommands.Contains(normalizedCommand),
            _ => false
        };
    }
}
