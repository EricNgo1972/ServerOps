using System.Text;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Configuration;
using ServerOps.Infrastructure.Host;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class ServiceRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_Uses_Sc_Create_On_Windows()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("C:/apps/AuthService/current/AuthService.exe");
        var runner = new FakeCommandRunner();
        var service = new ServiceRegistrationService(
            runner,
            fileSystem,
            new FakeRuntimeEnvironment(OsType.Windows),
            Options.Create(new ServiceRegistrationOptions()));

        await service.RegisterAsync("AuthService", "C:/apps/AuthService/current");

        var command = Assert.Single(runner.Commands);
        Assert.Equal("sc", command.Command);
        Assert.Equal("create", command.Arguments[0]);
        Assert.Equal("AuthService", command.Arguments[1]);
        Assert.Contains("binPath=", command.Arguments[2], StringComparison.Ordinal);
        Assert.Equal("start=auto", command.Arguments[3]);
        Assert.Equal("obj=LocalSystem", command.Arguments[4]);
    }

    [Fact]
    public async Task RegisterAsync_Writes_Systemd_Unit_And_Enables_On_Linux()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("/apps/AuthService/current/AuthService.dll");
        var runner = new FakeCommandRunner(
            new CommandResult { ExitCode = 0, StdOut = "ok" },
            new CommandResult { ExitCode = 0, StdOut = "ok" });
        var service = new ServiceRegistrationService(
            runner,
            fileSystem,
            new FakeRuntimeEnvironment(OsType.Linux),
            Options.Create(new ServiceRegistrationOptions
            {
                LinuxAppUser = "serverops-app"
            }));

        var result = await service.RegisterAsync("AuthService", "/apps/AuthService/current");

        Assert.True(result.Succeeded);
        Assert.Equal(2, runner.Commands.Count);
        Assert.Equal(new[] { "daemon-reload" }, runner.Commands[0].Arguments);
        Assert.Equal(new[] { "enable", "AuthService" }, runner.Commands[1].Arguments);
        Assert.Contains("User=serverops-app", fileSystem.ReadText("/etc/systemd/system/AuthService.service"));
        Assert.Contains("Group=serverops-app", fileSystem.ReadText("/etc/systemd/system/AuthService.service"));
        Assert.Contains("ExecStart=/usr/bin/dotnet /apps/AuthService/current/AuthService.dll", fileSystem.ReadText("/etc/systemd/system/AuthService.service"));
    }

    [Fact]
    public async Task ExistsAsync_Uses_Sc_Query_On_Windows()
    {
        var runner = new FakeCommandRunner(new CommandResult { ExitCode = 0, StdOut = "ok" });
        var service = new ServiceRegistrationService(
            runner,
            new FakeFileSystem(),
            new FakeRuntimeEnvironment(OsType.Windows),
            Options.Create(new ServiceRegistrationOptions()));

        var exists = await service.ExistsAsync("AuthService");

        Assert.True(exists);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("sc", command.Command);
        Assert.Equal(new[] { "query", "AuthService" }, command.Arguments);
    }

    [Fact]
    public async Task ExistsAsync_Uses_Systemctl_Show_On_Linux()
    {
        var runner = new FakeCommandRunner(new CommandResult { ExitCode = 0, StdOut = "LoadState=loaded" });
        var service = new ServiceRegistrationService(
            runner,
            new FakeFileSystem(),
            new FakeRuntimeEnvironment(OsType.Linux),
            Options.Create(new ServiceRegistrationOptions()));

        var exists = await service.ExistsAsync("authservice");

        Assert.True(exists);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("systemctl", command.Command);
        Assert.Equal(new[] { "show", "authservice", "--property", "LoadState" }, command.Arguments);
    }

    [Fact]
    public async Task UnregisterAsync_Uses_Sc_Delete_On_Windows()
    {
        var runner = new FakeCommandRunner(new CommandResult { ExitCode = 0, StdOut = "ok" });
        var service = new ServiceRegistrationService(
            runner,
            new FakeFileSystem(),
            new FakeRuntimeEnvironment(OsType.Windows),
            Options.Create(new ServiceRegistrationOptions()));

        var result = await service.UnregisterAsync("AuthService");

        Assert.True(result.Succeeded);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("sc", command.Command);
        Assert.Equal(new[] { "delete", "AuthService" }, command.Arguments);
    }

    [Fact]
    public async Task UnregisterAsync_Disables_Deletes_Unit_And_Reloads_On_Linux()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("/etc/systemd/system/AuthService.service");
        var runner = new FakeCommandRunner(
            new CommandResult { ExitCode = 0, StdOut = "disabled" },
            new CommandResult { ExitCode = 0, StdOut = "reloaded" });
        var service = new ServiceRegistrationService(
            runner,
            fileSystem,
            new FakeRuntimeEnvironment(OsType.Linux),
            Options.Create(new ServiceRegistrationOptions
            {
                LinuxAppUser = "serverops-app"
            }));

        var result = await service.UnregisterAsync("AuthService");

        Assert.True(result.Succeeded);
        Assert.Equal(2, runner.Commands.Count);
        Assert.Equal(new[] { "disable", "AuthService" }, runner.Commands[0].Arguments);
        Assert.Equal(new[] { "daemon-reload" }, runner.Commands[1].Arguments);
        Assert.False(fileSystem.FileExists("/etc/systemd/system/AuthService.service"));
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

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => _files.ContainsKey(path);
        public void DeleteFile(string path) => _files.Remove(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public void CreateDirectory(string path) => _directories.Add(path);
        public void DeleteDirectory(string path, bool recursive) => _directories.Remove(path);
        public void MoveDirectory(string sourcePath, string destinationPath) { }
        public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite) { }
        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
            => _files.Keys.Where(file => file.StartsWith(path, StringComparison.OrdinalIgnoreCase) && file.EndsWith(searchPattern[1..], StringComparison.OrdinalIgnoreCase)).ToList();
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            _files[path] = bytes;
            return Task.CompletedTask;
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(ReadText(path));

        public void AddFile(string path)
        {
            _files[path] = Encoding.UTF8.GetBytes("content");
        }

        public string ReadText(string path) => _files.TryGetValue(path, out var bytes) ? Encoding.UTF8.GetString(bytes) : string.Empty;
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
