using System.Net;
using System.Text;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Enums;
using ServerOps.Infrastructure.Deployment;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class DeploymentServiceTests
{
    [Fact]
    public async Task DeployAsync_Happy_Path_Succeeds()
    {
        var fileSystem = new FakeFileSystem();
        var archiveService = new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll"));
        var service = CreateService(
            fileSystem,
            archiveService,
            new FakeServiceControlService(
                startResult: new CommandResult { ExitCode = 0 },
                stopResult: new CommandResult { ExitCode = 0 }),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Succeeded, result.Status);
        Assert.Equal(DeploymentStage.Completed, result.Stage);
    }

    [Fact]
    public async Task DeployAsync_Invalid_Package_Fails_Before_Stop()
    {
        var control = new FakeServiceControlService();
        var service = CreateService(
            new FakeFileSystem(),
            new FakeArchiveService(_ => { }),
            control,
            new FakeDeploymentPackageValidator(false),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Equal(DeploymentStage.ValidatingPackage, result.Stage);
        Assert.Equal(0, control.StopCalls);
    }

    [Fact]
    public async Task DeployAsync_Stop_Failure_Aborts_Deployment()
    {
        var control = new FakeServiceControlService(stopResult: new CommandResult { ExitCode = 1, StdErr = "stop failed" });
        var service = CreateService(
            new FakeFileSystem(),
            new FakeArchiveService(_ => { }),
            control,
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Equal(DeploymentStage.StoppingService, result.Stage);
        Assert.Equal(0, control.StartCalls);
    }

    [Fact]
    public async Task DeployAsync_Start_Failure_Triggers_Rollback()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.CreateDirectory("/apps/phoebus-api/current");
        fileSystem.AddFile("/apps/phoebus-api/current/old.dll");

        var control = new FakeServiceControlService(
            startResults:
            [
                new CommandResult { ExitCode = 1, StdErr = "start failed" },
                new CommandResult { ExitCode = 0 }
            ],
            stopResult: new CommandResult { ExitCode = 0 });

        var service = CreateService(
            fileSystem,
            new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll")),
            control,
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.RolledBack, result.Status);
        Assert.Equal(DeploymentStage.RolledBack, result.Stage);
        Assert.Equal(2, control.StartCalls);
        Assert.True(fileSystem.DirectoryExists("/apps/phoebus-api/current"));
    }

    [Fact]
    public async Task DeployAsync_Health_Verification_Failure_Triggers_Rollback()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.CreateDirectory("/apps/phoebus-api/current");
        fileSystem.AddFile("/apps/phoebus-api/current/old.dll");

        var control = new FakeServiceControlService(
            startResults:
            [
                new CommandResult { ExitCode = 0 },
                new CommandResult { ExitCode = 0 }
            ],
            stopResult: new CommandResult { ExitCode = 0 });

        var service = CreateService(
            fileSystem,
            new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll")),
            control,
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(false),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.RolledBack, result.Status);
        Assert.Equal(DeploymentStage.RolledBack, result.Stage);
    }

    [Fact]
    public async Task DeployAsync_First_Deployment_With_No_Current_Folder_Succeeds()
    {
        var service = CreateService(
            new FakeFileSystem(),
            new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll")),
            new FakeServiceControlService(
                startResult: new CommandResult { ExitCode = 0 },
                stopResult: new CommandResult { ExitCode = 0 }),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task DeployAsync_Rollback_Failure_Returns_Failed_Result()
    {
        var control = new FakeServiceControlService(
            startResult: new CommandResult { ExitCode = 1, StdErr = "start failed" },
            stopResult: new CommandResult { ExitCode = 0 });

        var service = CreateService(
            new FakeFileSystem(),
            new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll")),
            control,
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Equal(DeploymentStage.Failed, result.Stage);
        Assert.Contains("Rollback failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DeploymentService CreateService(
        FakeFileSystem fileSystem,
        FakeArchiveService archiveService,
        FakeServiceControlService serviceControlService,
        FakeDeploymentPackageValidator packageValidator,
        FakeHealthVerificationService healthVerificationService,
        FakeAppTopologyService appTopologyService)
    {
        return new DeploymentService(
            new FakeHttpClientFactory(),
            fileSystem,
            archiveService,
            serviceControlService,
            new FakeRuntimeEnvironment(),
            packageValidator,
            healthVerificationService,
            appTopologyService);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "")
            => new(new StaticHttpMessageHandler()) { BaseAddress = new Uri("https://example.com") };
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("zip"))
            });
        }
    }

    private sealed class FakeRuntimeEnvironment : IRuntimeEnvironment
    {
        public ServerOps.Domain.Enums.OsType GetCurrentOs() => ServerOps.Domain.Enums.OsType.Linux;
        public string GetAppsRootPath() => "/apps";
        public string GetCloudflaredConfigPath() => "/etc/cloudflared/config.yml";
    }

    private sealed class FakeArchiveService : IArchiveService
    {
        private readonly Action<FakeFileSystem> _onExtract;
        private FakeFileSystem? _fileSystem;

        public FakeArchiveService(Action<FakeFileSystem> onExtract)
        {
            _onExtract = onExtract;
        }

        public void Bind(FakeFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken = default)
        {
            _fileSystem?.CreateDirectory(destinationPath);
            _fileSystem?.SetCurrentExtractPath(destinationPath);
            if (_fileSystem is not null)
            {
                _onExtract(_fileSystem);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceControlService : IServiceControlService
    {
        private readonly Queue<CommandResult> _startResults = new();
        private readonly CommandResult _stopResult;

        public FakeServiceControlService(
            CommandResult? startResult = null,
            CommandResult? stopResult = null,
            IEnumerable<CommandResult>? startResults = null)
        {
            _stopResult = stopResult ?? new CommandResult { ExitCode = 0 };

            if (startResults is not null)
            {
                foreach (var result in startResults)
                {
                    _startResults.Enqueue(result);
                }
            }
            else
            {
                _startResults.Enqueue(startResult ?? new CommandResult { ExitCode = 0 });
            }
        }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public Task<CommandResult> StartAsync(string serviceName, CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(_startResults.Count > 0 ? _startResults.Dequeue() : new CommandResult { ExitCode = 0 });
        }

        public Task<CommandResult> StopAsync(string serviceName, CancellationToken ct = default)
        {
            StopCalls++;
            return Task.FromResult(_stopResult);
        }

        public Task<CommandResult> RestartAsync(string serviceName, CancellationToken ct = default)
            => Task.FromResult(new CommandResult { ExitCode = 0 });
    }

    private sealed class FakeDeploymentPackageValidator : IDeploymentPackageValidator
    {
        private readonly bool _isValid;

        public FakeDeploymentPackageValidator(bool isValid)
        {
            _isValid = isValid;
        }

        public Task<bool> IsValidAsync(string extractedPath, CancellationToken ct = default)
            => Task.FromResult(_isValid);
    }

    private sealed class FakeHealthVerificationService : IHealthVerificationService
    {
        private readonly bool _isHealthy;

        public FakeHealthVerificationService(bool isHealthy)
        {
            _isHealthy = isHealthy;
        }

        public Task<bool> VerifyAsync(string appName, CancellationToken ct = default)
            => Task.FromResult(_isHealthy);
    }

    private sealed class FakeAppTopologyService : IAppTopologyService
    {
        private readonly IReadOnlyList<ServiceTopology> _topology;

        public FakeAppTopologyService(IReadOnlyList<ServiceTopology> topology)
        {
            _topology = topology;
        }

        public Task<IReadOnlyList<ServiceTopology>> GetTopologyAsync(CancellationToken ct = default)
            => Task.FromResult(_topology);
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private string _currentExtractPath = string.Empty;

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => _files.ContainsKey(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public void CreateDirectory(string path)
        {
            EnsureParents(path);
            _directories.Add(path);
        }
        public void DeleteDirectory(string path, bool recursive)
        {
            var directories = _directories.Where(item => item.Equals(path, StringComparison.OrdinalIgnoreCase) || item.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var directory in directories)
            {
                _directories.Remove(directory);
            }

            var files = _files.Keys.Where(item => item.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var file in files)
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

            var directories = _directories
                .Where(item => item.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) || item.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var directory in directories)
            {
                var relative = directory[sourcePath.Length..].TrimStart('/');
                var target = string.IsNullOrWhiteSpace(relative) ? destinationPath : Combine(destinationPath, relative);
                CreateDirectory(target);
            }

            var files = _files
                .Where(item => item.Key.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in files)
            {
                var relative = file.Key[sourcePath.Length..].TrimStart('/');
                _files[Combine(destinationPath, relative)] = file.Value.ToArray();
            }
        }
        public IReadOnlyList<string> GetDirectories(string path)
        {
            return _directories
                .Where(item =>
                {
                    var parent = Path.GetDirectoryName(item)?.Replace('\\', '/') ?? "/";
                    return string.Equals(parent, path, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
        {
            return _files.Keys
                .Where(file => file.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                .Where(file => MatchesPattern(Path.GetFileName(file), searchPattern))
                .ToList();
        }
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            EnsureParents(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/");
            _files[path] = bytes.ToArray();
            return Task.CompletedTask;
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_files.TryGetValue(path, out var bytes) ? Encoding.UTF8.GetString(bytes) : string.Empty);

        public void AddFile(string path)
        {
            var normalizedPath = path.Replace('\\', '/');
            EnsureParents(Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? "/");
            _files[normalizedPath] = Encoding.UTF8.GetBytes("content");
        }

        public void SetCurrentExtractPath(string path)
        {
            _currentExtractPath = path;
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

        private static bool MatchesPattern(string fileName, string searchPattern)
        {
            if (searchPattern == "*.dll")
            {
                return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            }

            if (searchPattern == "*.exe")
            {
                return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            }

            if (searchPattern == "*.deps.json")
            {
                return fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
