using System.Diagnostics;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using Microsoft.Extensions.Logging;

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
        "powershell",
        "where"
    };

    private readonly IRuntimeEnvironment _runtimeEnvironment;
    private readonly ILogger<CommandRunner> _logger;

    public CommandRunner(IRuntimeEnvironment runtimeEnvironment, ILogger<CommandRunner> logger)
    {
        _runtimeEnvironment = runtimeEnvironment;
        _logger = logger;
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

        var isRoutineNoise = IsRoutineNoise(command, request.Arguments);
        if (!isRoutineNoise)
        {
            _logger.LogInformation(
                "CommandRunner exec command={Command} args={Arguments}",
                command,
                string.Join(" | ", request.Arguments));
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new List<string>();
        var stdErr = new List<string>();
        var callbackTasks = new List<Task>();
        var stdOutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdErrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                stdOutClosed.TrySetResult();
                return;
            }

            lock (stdOut)
            {
                stdOut.Add(args.Data);
            }

            if (request.OnOutput is not null)
            {
                lock (callbackTasks)
                {
                    callbackTasks.Add(request.OnOutput(args.Data));
                }
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                stdErrClosed.TrySetResult();
                return;
            }

            lock (stdErr)
            {
                stdErr.Add(args.Data);
            }

            if (request.OnOutput is not null)
            {
                lock (callbackTasks)
                {
                    callbackTasks.Add(request.OnOutput(args.Data));
                }
            }
        };

        process.Start();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdOutClosed.Task, stdErrClosed.Task);
        await Task.WhenAll(callbackTasks);

        var stdOutText = string.Join(Environment.NewLine, stdOut);
        var stdErrText = string.Join(Environment.NewLine, stdErr);

        if (!isRoutineNoise)
        {
            _logger.LogInformation(
                "CommandRunner result command={Command} exitCode={ExitCode} stdout={StdOut} stderr={StdErr}",
                command,
                process.ExitCode,
                string.IsNullOrWhiteSpace(stdOutText) ? "-" : stdOutText,
                string.IsNullOrWhiteSpace(stdErrText) ? "-" : stdErrText);
        }

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdOutText,
            StdErr = stdErrText
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

    private static bool IsRoutineNoise(string command, IReadOnlyList<string> arguments)
    {
        if (command.Equals("where", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("netstat", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!command.Equals("sc", StringComparison.OrdinalIgnoreCase) || arguments.Count == 0)
        {
            return false;
        }

        return arguments[0].Equals("query", StringComparison.OrdinalIgnoreCase) ||
               arguments[0].Equals("queryex", StringComparison.OrdinalIgnoreCase) ||
               arguments[0].Equals("qc", StringComparison.OrdinalIgnoreCase);
    }

}
