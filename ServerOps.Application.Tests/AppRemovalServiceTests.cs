using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Application.Services;
using Xunit;

namespace ServerOps.Application.Tests;

public sealed class AppRemovalServiceTests
{
    [Fact]
    public async Task RemoveAsync_Removes_Exposure_Service_And_App_Files()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddDirectory("/apps/AuthService");
        var exposure = new FakeExposureService();
        var control = new FakeServiceControlService(new CommandResult { ExitCode = 0 });
        var registration = new FakeServiceRegistrationService(exists: true, unregisterResult: new CommandResult { ExitCode = 0 });
        var service = CreateService(exposure, control, registration, fileSystem);

        var result = await service.RemoveAsync("AuthService");

        Assert.True(result.Succeeded);
        Assert.Equal("AuthService", exposure.LastServiceName);
        Assert.Equal("AuthService", control.LastStoppedServiceName);
        Assert.Equal("AuthService", registration.LastUnregisteredServiceName);
        Assert.False(fileSystem.DirectoryExists("/apps/AuthService"));
    }

    [Fact]
    public async Task RemoveAsync_Allows_Already_Stopped_Service()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddDirectory("/apps/AuthService");
        var control = new FakeServiceControlService(new CommandResult { ExitCode = 1062, StdErr = "FAILED 1062" });
        var registration = new FakeServiceRegistrationService(exists: true, unregisterResult: new CommandResult { ExitCode = 0 });
        var service = CreateService(new FakeExposureService(), control, registration, fileSystem);

        var result = await service.RemoveAsync("AuthService");

        Assert.True(result.Succeeded);
        Assert.Equal(1, control.StopCalls);
        Assert.Equal(1, registration.UnregisterCalls);
    }

    [Fact]
    public async Task RemoveAsync_Stop_Failure_Aborts_Removal()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.AddDirectory("/apps/AuthService");
        var control = new FakeServiceControlService(new CommandResult { ExitCode = 1, StdErr = "stop failed" });
        var registration = new FakeServiceRegistrationService(exists: true, unregisterResult: new CommandResult { ExitCode = 0 });
        var service = CreateService(new FakeExposureService(), control, registration, fileSystem);

        var result = await service.RemoveAsync("AuthService");

        Assert.False(result.Succeeded);
        Assert.Contains("Failed to stop service.", result.StdErr, StringComparison.Ordinal);
        Assert.True(fileSystem.DirectoryExists("/apps/AuthService"));
        Assert.Equal(0, registration.UnregisterCalls);
    }

    [Fact]
    public async Task RemoveAsync_Succeeds_When_Service_And_Files_Are_Missing()
    {
        var service = CreateService(
            new FakeExposureService(),
            new FakeServiceControlService(new CommandResult { ExitCode = 0 }),
            new FakeServiceRegistrationService(exists: false, unregisterResult: new CommandResult { ExitCode = 0 }),
            new FakeFileSystem());

        var result = await service.RemoveAsync("AuthService");

        Assert.True(result.Succeeded);
        Assert.Contains("Service not installed", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("App files already removed", result.StdOut, StringComparison.Ordinal);
    }

    private static AppRemovalService CreateService(
        FakeExposureService exposure,
        FakeServiceControlService control,
        FakeServiceRegistrationService registration,
        FakeFileSystem fileSystem)
    {
        return new AppRemovalService(
            exposure,
            control,
            registration,
            fileSystem,
            new FakeRuntimeEnvironment(),
            new FakeOperationLogger());
    }

    private sealed class FakeExposureService : IExposureService
    {
        public string LastServiceName { get; private set; } = string.Empty;

        public Task ExposeAsync(string serviceName, string hostname, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(string serviceName, string newHostname, CancellationToken ct = default) => Task.CompletedTask;

        public Task UnexposeAsync(string serviceName, CancellationToken ct = default)
        {
            LastServiceName = serviceName;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceControlService : IServiceControlService
    {
        private readonly CommandResult _stopResult;

        public FakeServiceControlService(CommandResult stopResult)
        {
            _stopResult = stopResult;
        }

        public int StopCalls { get; private set; }
        public string LastStoppedServiceName { get; private set; } = string.Empty;

        public Task<CommandResult> StartAsync(string serviceName, CancellationToken ct = default)
            => Task.FromResult(new CommandResult { ExitCode = 0 });

        public Task<CommandResult> StopAsync(string serviceName, CancellationToken ct = default)
        {
            StopCalls++;
            LastStoppedServiceName = serviceName;
            return Task.FromResult(_stopResult);
        }

        public Task<CommandResult> RestartAsync(string serviceName, CancellationToken ct = default)
            => Task.FromResult(new CommandResult { ExitCode = 0 });
    }

    private sealed class FakeServiceRegistrationService : IServiceRegistrationService
    {
        private readonly bool _exists;
        private readonly CommandResult _unregisterResult;

        public FakeServiceRegistrationService(bool exists, CommandResult unregisterResult)
        {
            _exists = exists;
            _unregisterResult = unregisterResult;
        }

        public int UnregisterCalls { get; private set; }
        public string LastUnregisteredServiceName { get; private set; } = string.Empty;

        public Task<bool> ExistsAsync(string serviceName, CancellationToken ct = default)
            => Task.FromResult(_exists);

        public Task<CommandResult> RegisterAsync(string serviceName, string deploymentPath, CancellationToken ct = default)
            => Task.FromResult(new CommandResult { ExitCode = 0 });

        public Task<CommandResult> UnregisterAsync(string serviceName, CancellationToken ct = default)
        {
            UnregisterCalls++;
            LastUnregisteredServiceName = serviceName;
            return Task.FromResult(_unregisterResult);
        }
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => false;
        public void DeleteFile(string path) { }
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public void CreateDirectory(string path) => _directories.Add(path);
        public void DeleteDirectory(string path, bool recursive) => _directories.Remove(path);
        public void MoveDirectory(string sourcePath, string destinationPath) { }
        public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite) { }
        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) => Array.Empty<string>();
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public void AddDirectory(string path) => _directories.Add(path);
    }

    private sealed class FakeRuntimeEnvironment : IRuntimeEnvironment
    {
        public ServerOps.Domain.Enums.OsType GetCurrentOs() => ServerOps.Domain.Enums.OsType.Linux;
        public string GetAppsRootPath() => "/apps";
        public string GetCloudflaredConfigPath() => "/etc/cloudflared/config.yml";
        public string GetSystemdServiceDirectory() => "/etc/systemd/system";
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
