using ServerOps.Application.DTOs;
using ServerOps.Application.Abstractions;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Host;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class ServiceControlServiceTests
{
    [Fact]
    public async Task StartAsync_Uses_Systemctl_On_Linux()
    {
        var runner = new FakeCommandRunner();
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Linux), new FakeOperationLogger());

        await service.StartAsync("phoebus");

        var command = Assert.Single(runner.Commands);
        Assert.Equal("systemctl", command.Command);
        Assert.Equal(new[] { "start", "phoebus" }, command.Arguments);
    }

    [Fact]
    public async Task StopAsync_Uses_Systemctl_On_Linux()
    {
        var runner = new FakeCommandRunner();
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Linux), new FakeOperationLogger());

        await service.StopAsync("phoebus");

        var command = Assert.Single(runner.Commands);
        Assert.Equal("systemctl", command.Command);
        Assert.Equal(new[] { "stop", "phoebus" }, command.Arguments);
    }

    [Fact]
    public async Task RestartAsync_Uses_Systemctl_Restart_On_Linux()
    {
        var runner = new FakeCommandRunner();
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Linux), new FakeOperationLogger());

        await service.RestartAsync("phoebus");

        var command = Assert.Single(runner.Commands);
        Assert.Equal("systemctl", command.Command);
        Assert.Equal(new[] { "restart", "phoebus" }, command.Arguments);
    }

    [Fact]
    public async Task StartAsync_Uses_Sc_On_Windows()
    {
        var runner = new FakeCommandRunner();
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Windows), new FakeOperationLogger());

        await service.StartAsync("Spooler");

        var command = Assert.Single(runner.Commands);
        Assert.Equal("sc", command.Command);
        Assert.Equal(new[] { "start", "Spooler" }, command.Arguments);
    }

    [Fact]
    public async Task StopAsync_Uses_Sc_On_Windows()
    {
        var runner = new FakeCommandRunner();
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Windows), new FakeOperationLogger());

        await service.StopAsync("Spooler");

        var command = Assert.Single(runner.Commands);
        Assert.Equal("sc", command.Command);
        Assert.Equal(new[] { "stop", "Spooler" }, command.Arguments);
    }

    [Fact]
    public async Task RestartAsync_Uses_Stop_Then_Start_On_Windows()
    {
        var runner = new FakeCommandRunner(
            new CommandResult { ExitCode = 0, StdOut = "stop issued" },
            new CommandResult { ExitCode = 0, StdOut = "STATE : 1  STOPPED" },
            new CommandResult { ExitCode = 0, StdOut = "start issued" });
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Windows), new FakeOperationLogger());

        await service.RestartAsync("Spooler");

        Assert.Equal(3, runner.Commands.Count);
        Assert.Equal("sc", runner.Commands[0].Command);
        Assert.Equal(new[] { "stop", "Spooler" }, runner.Commands[0].Arguments);
        Assert.Equal("sc", runner.Commands[1].Command);
        Assert.Equal(new[] { "query", "Spooler" }, runner.Commands[1].Arguments);
        Assert.Equal("sc", runner.Commands[2].Command);
        Assert.Equal(new[] { "start", "Spooler" }, runner.Commands[2].Arguments);
    }

    [Fact]
    public async Task RestartAsync_Waits_For_StopPending_Before_Start_On_Windows()
    {
        var runner = new FakeCommandRunner(
            new CommandResult { ExitCode = 0, StdOut = "stop issued" },
            new CommandResult { ExitCode = 0, StdOut = "STATE : 3  STOP_PENDING" },
            new CommandResult { ExitCode = 0, StdOut = "STATE : 3  STOP_PENDING" },
            new CommandResult { ExitCode = 0, StdOut = "STATE : 1  STOPPED" },
            new CommandResult { ExitCode = 0, StdOut = "start issued" });
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Windows), new FakeOperationLogger());

        await service.RestartAsync("Spooler");

        Assert.Equal(5, runner.Commands.Count);
        Assert.Equal(new[] { "stop", "Spooler" }, runner.Commands[0].Arguments);
        Assert.Equal(new[] { "query", "Spooler" }, runner.Commands[1].Arguments);
        Assert.Equal(new[] { "query", "Spooler" }, runner.Commands[2].Arguments);
        Assert.Equal(new[] { "query", "Spooler" }, runner.Commands[3].Arguments);
        Assert.Equal("sc", runner.Commands[4].Command);
        Assert.Equal(new[] { "start", "Spooler" }, runner.Commands[4].Arguments);
    }

    [Fact]
    public async Task Invalid_Service_Name_Throws_ArgumentException()
    {
        var runner = new FakeCommandRunner();
        var service = new ServiceControlService(runner, new FakeRuntimeEnvironment(OsType.Windows), new FakeOperationLogger());

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync("   "));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StopAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RestartAsync(" "));
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        private readonly Queue<CommandResult> _results;
        public List<CommandRequest> Commands { get; } = [];

        public FakeCommandRunner(params CommandResult[] results)
        {
            _results = new Queue<CommandResult>(results);
        }

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            Commands.Add(request);

            if (_results.Count > 0)
            {
                return Task.FromResult(_results.Dequeue());
            }

            return Task.FromResult(new CommandResult
            {
                ExitCode = 0,
                StdOut = "ok",
                StdErr = string.Empty
            });
        }
    }

    private sealed class FakeRuntimeEnvironment : IRuntimeEnvironment
    {
        private readonly OsType _osType;

        public FakeRuntimeEnvironment(OsType osType)
        {
            _osType = osType;
        }

        public OsType GetCurrentOs() => _osType;

        public string GetAppsRootPath() => string.Empty;

        public string GetCloudflaredConfigPath() => string.Empty;

        public string GetSystemdServiceDirectory() => string.Empty;
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
