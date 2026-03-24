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
            new FakeServiceRegistrationService(fileSystem),
            new FakeServicePermissionService(),
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
            new FakeServiceRegistrationService(new FakeFileSystem()),
            new FakeServicePermissionService(),
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
            new FakeServiceRegistrationService(new FakeFileSystem(), exists: true),
            new FakeServicePermissionService(),
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
            new FakeServiceRegistrationService(fileSystem),
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.RolledBack, result.Status);
        Assert.Equal(DeploymentStage.RolledBack, result.Stage);
        Assert.Equal(2, control.StartCalls);
        Assert.Equal(2, control.StopCalls);
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
            new FakeServiceRegistrationService(fileSystem),
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService([false, false, false, false, false, false, false, false, false, false, true]),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.RolledBack, result.Status);
        Assert.Equal(DeploymentStage.RolledBack, result.Stage);
    }

    [Fact]
    public async Task DeployAsync_First_Deployment_With_No_Current_Folder_Succeeds()
    {
        var fileSystem = new FakeFileSystem();
        var registration = new FakeServiceRegistrationService(fileSystem);
        var service = CreateService(
            fileSystem,
            new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll")),
            new FakeServiceControlService(
                startResult: new CommandResult { ExitCode = 0 },
                stopResult: new CommandResult { ExitCode = 0 }),
            registration,
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Succeeded, result.Status);
        Assert.Equal(1, registration.RegisterCalls);
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
            new FakeServiceRegistrationService(new FakeFileSystem()),
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Equal(DeploymentStage.Failed, result.Stage);
        Assert.Contains("Rollback failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeployAsync_Permission_Failure_Returns_Failed_Result()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.CreateDirectory("/apps/phoebus-api/current");
        fileSystem.AddFile("/apps/phoebus-api/current/old.dll");

        var service = CreateService(
            fileSystem,
            new FakeArchiveService(fs => fs.AddFile("/tmp/serverops/deployment/extracted/app.dll")),
            new FakeServiceControlService(
                startResult: new CommandResult { ExitCode = 0 },
                stopResult: new CommandResult { ExitCode = 0 }),
            new FakeServiceRegistrationService(fileSystem),
            new FakeServicePermissionService(new CommandResult { ExitCode = 1, StdErr = "permission failed" }),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5000] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip");

        Assert.Equal(DeploymentStatus.RolledBack, result.Status);
    }

    [Fact]
    public async Task DeployAsync_Port_Override_Rewrites_Kestrel_Http_Url()
    {
        var fileSystem = new FakeFileSystem();
        var archiveService = new FakeArchiveService(async fs =>
        {
            fs.AddFile("app.dll");
            await fs.WriteAllBytesAsync(
                fs.Combine(fs.CurrentExtractPath, "appsettings.json"),
                Encoding.UTF8.GetBytes("""
                {
                  "Kestrel": {
                    "Endpoints": {
                      "Http": {
                        "Url": "http://*:5000"
                      }
                    }
                  }
                }
                """));
        });
        var service = CreateService(
            fileSystem,
            archiveService,
            new FakeServiceControlService(
                startResult: new CommandResult { ExitCode = 0 },
                stopResult: new CommandResult { ExitCode = 0 }),
            new FakeServiceRegistrationService(fileSystem),
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5200] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip", 5200);

        Assert.Equal(DeploymentStatus.Succeeded, result.Status);
        var content = await fileSystem.ReadAllTextAsync("/apps/phoebus-api/current/appsettings.json");
        Assert.Contains("\"Url\": \"http://*:5200\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_Port_Override_Adds_Default_Kestrel_When_Missing()
    {
        var fileSystem = new FakeFileSystem();
        var archiveService = new FakeArchiveService(async fs =>
        {
            fs.AddFile("app.dll");
            await fs.WriteAllBytesAsync(
                fs.Combine(fs.CurrentExtractPath, "appsettings.json"),
                Encoding.UTF8.GetBytes("""{ "Logging": { "LogLevel": { "Default": "Information" } } }"""));
        });
        var service = CreateService(
            fileSystem,
            archiveService,
            new FakeServiceControlService(
                startResult: new CommandResult { ExitCode = 0 },
                stopResult: new CommandResult { ExitCode = 0 }),
            new FakeServiceRegistrationService(fileSystem),
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([new ServiceTopology { ServiceName = "phoebus-api", Status = ServiceStatus.Running, Ports = [5200] }]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip", 5200);

        Assert.Equal(DeploymentStatus.Succeeded, result.Status);
        var content = await fileSystem.ReadAllTextAsync("/apps/phoebus-api/current/appsettings.json");
        Assert.Contains("\"Kestrel\"", content, StringComparison.Ordinal);
        Assert.Contains("\"Url\": \"http://0.0.0.0:5200\"", content, StringComparison.Ordinal);
        Assert.Contains("\"MaxRequestBodySize\": 52428800", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeployAsync_Port_Override_Missing_Kestrel_Http_Url_Fails_Before_Stop()
    {
        var fileSystem = new FakeFileSystem();
        var control = new FakeServiceControlService();
        var archiveService = new FakeArchiveService(async fs =>
        {
            fs.AddFile("app.dll");
            await fs.WriteAllBytesAsync(
                fs.Combine(fs.CurrentExtractPath, "appsettings.json"),
                Encoding.UTF8.GetBytes("""{ "Kestrel": { "Endpoints": { "Https": { "Url": "https://*:5001" } } } }"""));
        });
        var service = CreateService(
            fileSystem,
            archiveService,
            control,
            new FakeServiceRegistrationService(fileSystem, exists: true),
            new FakeServicePermissionService(),
            new FakeDeploymentPackageValidator(true),
            new FakeHealthVerificationService(true),
            new FakeAppTopologyService([]));

        var result = await service.DeployAsync("phoebus-api", "https://example.com/app.zip", 5200);

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Equal(DeploymentStage.ValidatingPackage, result.Stage);
        Assert.Equal(0, control.StopCalls);
        Assert.Contains("Kestrel:Endpoints:Http:Url", result.Message, StringComparison.Ordinal);
    }

    private static DeploymentService CreateService(
        FakeFileSystem fileSystem,
        FakeArchiveService archiveService,
        FakeServiceControlService serviceControlService,
        FakeServiceRegistrationService serviceRegistrationService,
        FakeServicePermissionService servicePermissionService,
        FakeDeploymentPackageValidator packageValidator,
        FakeHealthVerificationService healthVerificationService,
        FakeAppTopologyService appTopologyService)
    {
        archiveService.Bind(fileSystem);

        return new DeploymentService(
            new FakeHttpClientFactory(),
            fileSystem,
            archiveService,
            serviceControlService,
            serviceRegistrationService,
            servicePermissionService,
            new FakeRuntimeEnvironment(),
            packageValidator,
            healthVerificationService,
            appTopologyService,
            new FakeDeploymentHistoryStore(),
            new FakeOperationLogger());
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
        public string GetSystemdServiceDirectory() => "/etc/systemd/system";
    }

    private sealed class FakeArchiveService : IArchiveService
    {
        private readonly Func<FakeFileSystem, Task> _onExtract;
        private FakeFileSystem? _fileSystem;

        public FakeArchiveService(Action<FakeFileSystem> onExtract)
            : this(fs =>
            {
                onExtract(fs);
                return Task.CompletedTask;
            })
        {
        }

        public FakeArchiveService(Func<FakeFileSystem, Task> onExtract)
        {
            _onExtract = onExtract;
        }

        public void Bind(FakeFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public async Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken = default)
        {
            _fileSystem?.CreateDirectory(destinationPath);
            _fileSystem?.SetCurrentExtractPath(destinationPath);
            if (_fileSystem is not null)
            {
                await _onExtract(_fileSystem);
            }
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

    private sealed class FakeServiceRegistrationService : IServiceRegistrationService
    {
        private readonly FakeFileSystem _fileSystem;
        private readonly bool? _exists;

        public FakeServiceRegistrationService(FakeFileSystem fileSystem, bool? exists = null)
        {
            _fileSystem = fileSystem;
            _exists = exists;
        }

        public int ExistsCalls { get; private set; }
        public int RegisterCalls { get; private set; }

        public Task<bool> ExistsAsync(string serviceName, CancellationToken ct = default)
        {
            ExistsCalls++;
            return Task.FromResult(_exists ?? _fileSystem.DirectoryExists(_fileSystem.Combine("/apps", serviceName, "current")));
        }

        public Task<CommandResult> RegisterAsync(string serviceName, string deploymentPath, CancellationToken ct = default)
        {
            RegisterCalls++;
            return Task.FromResult(new CommandResult { ExitCode = 0 });
        }

        public Task<CommandResult> UnregisterAsync(string serviceName, CancellationToken ct = default)
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

    private sealed class FakeServicePermissionService : IServicePermissionService
    {
        private readonly CommandResult _result;

        public FakeServicePermissionService(CommandResult? result = null)
        {
            _result = result ?? new CommandResult { ExitCode = 0 };
        }

        public int Calls { get; private set; }

        public Task<CommandResult> EnsureRuntimePermissionsAsync(string serviceName, string deploymentPath, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeHealthVerificationService : IHealthVerificationService
    {
        private readonly Queue<bool> _values = new();

        public FakeHealthVerificationService(bool isHealthy)
        {
            _values.Enqueue(isHealthy);
        }

        public FakeHealthVerificationService(IEnumerable<bool> values)
        {
            foreach (var value in values)
            {
                _values.Enqueue(value);
            }
        }

        public Task<bool> VerifyAsync(string appName, CancellationToken ct = default)
            => Task.FromResult(_values.Count > 1 ? _values.Dequeue() : _values.Peek());
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

    private sealed class FakeDeploymentHistoryStore : IDeploymentHistoryStore
    {
        public List<DeploymentHistoryItem> Items { get; } = [];

        public Task<IReadOnlyList<DeploymentHistoryItem>> GetByAppAsync(string appName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DeploymentHistoryItem>>(Items.Where(x => x.AppName == appName).ToList());

        public Task AppendAsync(DeploymentHistoryItem item, CancellationToken ct = default)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private string _currentExtractPath = string.Empty;

        public string CurrentExtractPath => _currentExtractPath;

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => _files.ContainsKey(path);
        public void DeleteFile(string path) => _files.Remove(path);
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
            if (!normalizedPath.StartsWith("/", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_currentExtractPath))
            {
                normalizedPath = Combine(_currentExtractPath, normalizedPath);
            }
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
            if (string.Equals(searchPattern, "appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase);
            }

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
