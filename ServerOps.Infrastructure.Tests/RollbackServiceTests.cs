using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Deployment;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class RollbackServiceTests
{
    [Fact]
    public async Task RollbackAsync_Succeeds()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.CreateDirectory("/apps/phoebus-api/current");
        fileSystem.AddFile("/apps/phoebus-api/current/live.dll");
        fileSystem.CreateDirectory("/apps/phoebus-api/backup_20260101_120000");
        fileSystem.AddFile("/apps/phoebus-api/backup_20260101_120000/app.dll");

        var store = new FakeDeploymentHistoryStore([
            new DeploymentHistoryItem
            {
                DeploymentId = "dep-1",
                AppName = "phoebus-api",
                Version = "1.0.0",
                Status = DeploymentStatus.Succeeded,
                Stage = DeploymentStage.Completed,
                BackupPath = "/apps/phoebus-api/backup_20260101_120000",
                StartedAtUtc = DateTimeOffset.UtcNow
            }
        ]);

        var service = CreateService(
            fileSystem,
            store,
            new FakeServiceControlService(),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.RollbackAsync("phoebus-api", "dep-1");

        Assert.Equal(DeploymentStatus.RolledBack, result.Status);
        Assert.Contains(store.Items, x => x.IsRollback);
    }

    [Fact]
    public async Task RollbackAsync_Invalid_Id_Returns_Failed()
    {
        var service = CreateService(
            new FakeFileSystem(),
            new FakeDeploymentHistoryStore([]),
            new FakeServiceControlService(),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([]));

        var result = await service.RollbackAsync("phoebus-api", "missing");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RollbackAsync_Missing_Backup_Returns_Failed()
    {
        var store = new FakeDeploymentHistoryStore([
            new DeploymentHistoryItem
            {
                DeploymentId = "dep-1",
                AppName = "phoebus-api",
                Version = "1.0.0",
                Status = DeploymentStatus.Succeeded,
                Stage = DeploymentStage.Completed,
                BackupPath = "/apps/phoebus-api/missing",
                StartedAtUtc = DateTimeOffset.UtcNow
            }
        ]);

        var service = CreateService(
            new FakeFileSystem(),
            store,
            new FakeServiceControlService(),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([]));

        var result = await service.RollbackAsync("phoebus-api", "dep-1");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RollbackAsync_Failure_Recovery_Returns_Failed()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.CreateDirectory("/apps/phoebus-api/current");
        fileSystem.AddFile("/apps/phoebus-api/current/live.dll");
        fileSystem.CreateDirectory("/apps/phoebus-api/backup_20260101_120000");
        fileSystem.AddFile("/apps/phoebus-api/backup_20260101_120000/app.dll");

        var store = new FakeDeploymentHistoryStore([
            new DeploymentHistoryItem
            {
                DeploymentId = "dep-1",
                AppName = "phoebus-api",
                Version = "1.0.0",
                Status = DeploymentStatus.Succeeded,
                Stage = DeploymentStage.Completed,
                BackupPath = "/apps/phoebus-api/backup_20260101_120000",
                StartedAtUtc = DateTimeOffset.UtcNow
            }
        ]);

        var service = CreateService(
            fileSystem,
            store,
            new FakeServiceControlService(startResult: new CommandResult { ExitCode = 1, StdErr = "start failed" }),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.RollbackAsync("phoebus-api", "dep-1");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Contains("manual intervention", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(fileSystem.DirectoryExists("/apps/phoebus-api/current"));
    }

    private static RollbackService CreateService(
        FakeFileSystem fileSystem,
        FakeDeploymentHistoryStore store,
        FakeServiceControlService serviceControlService,
        FakeHealthVerificationService healthVerificationService,
        FakeAppTopologyService topologyService)
    {
        return new RollbackService(
            store,
            serviceControlService,
            fileSystem,
            new FakeRuntimeEnvironment(),
            healthVerificationService,
            topologyService,
            new FakeOperationLogger());
    }

    private sealed class FakeRuntimeEnvironment : IRuntimeEnvironment
    {
        public ServerOps.Domain.Enums.OsType GetCurrentOs() => ServerOps.Domain.Enums.OsType.Linux;
        public string GetAppsRootPath() => "/apps";
        public string GetCloudflaredConfigPath() => "/etc/cloudflared/config.yml";
        public string GetSystemdServiceDirectory() => "/etc/systemd/system";
    }

    private sealed class FakeDeploymentHistoryStore : IDeploymentHistoryStore
    {
        public List<DeploymentHistoryItem> Items { get; }

        public FakeDeploymentHistoryStore(IEnumerable<DeploymentHistoryItem> items)
        {
            Items = items.ToList();
        }

        public Task<IReadOnlyList<DeploymentHistoryItem>> GetByAppAsync(string appName, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DeploymentHistoryItem>>(Items.Where(x => x.AppName == appName).ToList());

        public Task AppendAsync(DeploymentHistoryItem item, CancellationToken ct)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceControlService : IServiceControlService
    {
        private readonly CommandResult _startResult;

        public FakeServiceControlService(CommandResult? startResult = null)
        {
            _startResult = startResult ?? new CommandResult { ExitCode = 0 };
        }

        public Task<CommandResult> StartAsync(string serviceName, string? operationId = null, CancellationToken ct = default) => Task.FromResult(_startResult);
        public Task<CommandResult> StopAsync(string serviceName, string? operationId = null, CancellationToken ct = default) => Task.FromResult(new CommandResult { ExitCode = 0 });
        public Task<CommandResult> RestartAsync(string serviceName, string? operationId = null, CancellationToken ct = default) => Task.FromResult(new CommandResult { ExitCode = 0 });
    }

    private sealed class FakeHealthVerificationService : IHealthVerificationService
    {
        private readonly bool _value;

        public FakeHealthVerificationService(bool value)
        {
            _value = value;
        }

        public Task<bool> VerifyAsync(string appName, CancellationToken ct = default) => Task.FromResult(_value);
    }

    private sealed class FakeAppTopologyService : IAppTopologyService
    {
        private readonly IReadOnlyList<ServiceTopology> _items;

        public FakeAppTopologyService(IReadOnlyList<ServiceTopology> items)
        {
            _items = items;
        }

        public Task<IReadOnlyList<ServiceTopology>> GetTopologyAsync(CancellationToken ct = default) => Task.FromResult(_items);
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => _files.Contains(path);
        public void DeleteFile(string path) => _files.Remove(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public void CreateDirectory(string path)
        {
            EnsureParents(path);
            _directories.Add(path);
        }
        public void DeleteDirectory(string path, bool recursive)
        {
            foreach (var directory in _directories.Where(x => x.Equals(path, StringComparison.OrdinalIgnoreCase) || x.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _directories.Remove(directory);
            }

            foreach (var file in _files.Where(x => x.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _files.Remove(file);
            }
        }
        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            CopyDirectory(sourcePath, destinationPath, overwrite: true);
            DeleteDirectory(sourcePath, recursive: true);
        }
        public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite)
        {
            if (!_directories.Contains(sourcePath))
            {
                return;
            }

            CreateDirectory(destinationPath);

            foreach (var directory in _directories.Where(x => x.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) || x.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var relative = directory[sourcePath.Length..].TrimStart('/');
                CreateDirectory(string.IsNullOrWhiteSpace(relative) ? destinationPath : Combine(destinationPath, relative));
            }

            foreach (var file in _files.Where(x => x.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var relative = file[sourcePath.Length..].TrimStart('/');
                _files.Add(Combine(destinationPath, relative));
            }
        }
        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) => Array.Empty<string>();
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public void AddFile(string path)
        {
            EnsureParents(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/");
            _files.Add(path);
        }

        private void EnsureParents(string path)
        {
            var normalizedPath = path.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath == "/")
            {
                _directories.Add("/");
                return;
            }

            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = normalizedPath.StartsWith("/", StringComparison.Ordinal) ? "/" : string.Empty;

            foreach (var part in parts)
            {
                current = string.IsNullOrEmpty(current) || current == "/"
                    ? $"{current}{part}".Replace("//", "/", StringComparison.Ordinal)
                    : $"{current}/{part}";
                _directories.Add(current);
            }
        }
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
