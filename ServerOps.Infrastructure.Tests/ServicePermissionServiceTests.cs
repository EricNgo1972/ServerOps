using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Configuration;
using ServerOps.Infrastructure.Host;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class ServicePermissionServiceTests
{
    [Fact]
    public async Task EnsureRuntimePermissionsAsync_Returns_LocalSystem_On_Windows()
    {
        var runner = new FakeCommandRunner();
        var service = new ServicePermissionService(
            runner,
            new FakeRuntimeEnvironment(OsType.Windows),
            Options.Create(new ServiceRegistrationOptions()));

        var result = await service.EnsureRuntimePermissionsAsync("AuthService", "C:/ServerOps/Apps/AuthService/current");

        Assert.True(result.Succeeded);
        Assert.Contains("LocalSystem", result.StdOut, StringComparison.Ordinal);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task EnsureRuntimePermissionsAsync_Creates_Linux_User_And_Chowns_Current()
    {
        var runner = new FakeCommandRunner(
            new CommandResult { ExitCode = 1, StdErr = "missing" },
            new CommandResult { ExitCode = 0, StdOut = "created" },
            new CommandResult { ExitCode = 0, StdOut = "chown ok" });
        var service = new ServicePermissionService(
            runner,
            new FakeRuntimeEnvironment(OsType.Linux),
            Options.Create(new ServiceRegistrationOptions
            {
                LinuxAppUser = "serverops-app"
            }));

        var result = await service.EnsureRuntimePermissionsAsync("authservice", "/apps/authservice/current");

        Assert.True(result.Succeeded);
        Assert.Equal(3, runner.Commands.Count);
        Assert.Equal("id", runner.Commands[0].Command);
        Assert.Equal("useradd", runner.Commands[1].Command);
        Assert.Equal("chown", runner.Commands[2].Command);
        Assert.Equal(new[] { "-R", "serverops-app:serverops-app", "/apps/authservice/current" }, runner.Commands[2].Arguments);
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
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new CommandResult { ExitCode = 0 });
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
        public string GetAppsRootPath() => "/apps";
        public string GetCloudflaredConfigPath() => "/etc/cloudflared/config.yml";
        public string GetSystemdServiceDirectory() => "/etc/systemd/system";
    }
}
