using System.Text;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;
using ServerOps.Infrastructure.CloudflareTunnel;
using ServerOps.Infrastructure.Configuration;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class CloudflaredConfigServiceTests
{
    [Fact]
    public async Task AddIngressAsync_Duplicate_Hostname_Does_Not_Insert_Duplicate()
    {
        var initialConfig = """
tunnel: test-tunnel
ingress:
- hostname: phoebus.local
  service: http://localhost:5000
- service: http_status:404
""";

        var fileSystem = new FakeFileSystem(initialConfig);
        var service = new CloudflaredConfigService(
            new FakeCloudflaredService(new TunnelInfo { ConfigPath = "/etc/cloudflared/config.yml" }),
            fileSystem,
            new FakeCommandRunner(),
            new FakeEndpointService(),
            new FakeHttpClientFactory(),
            Options.Create(new CloudflareOptions()));

        await service.AddIngressAsync("phoebus.local", 5000);

        Assert.Equal(initialConfig, fileSystem.Contents);
    }

    [Fact]
    public async Task RemoveIngressAsync_Removes_Existing_Hostname_Block()
    {
        var initialConfig = """
tunnel: test-tunnel
ingress:
- hostname: phoebus.local
  service: http://localhost:5000
- hostname: ocr.local
  service: http://localhost:5050
- service: http_status:404
""";

        var expectedConfig = """
tunnel: test-tunnel
ingress:
- hostname: ocr.local
  service: http://localhost:5050
- service: http_status:404
""" + Environment.NewLine;

        var fileSystem = new FakeFileSystem(initialConfig);
        var service = new CloudflaredConfigService(
            new FakeCloudflaredService(new TunnelInfo { ConfigPath = "/etc/cloudflared/config.yml" }),
            fileSystem,
            new FakeCommandRunner(),
            new FakeEndpointService(),
            new FakeHttpClientFactory(),
            Options.Create(new CloudflareOptions()));

        await service.RemoveIngressAsync("phoebus.local");

        Assert.Equal(NormalizeLineEndings(expectedConfig), NormalizeLineEndings(fileSystem.Contents));
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class FakeCloudflaredService : ICloudflaredService
    {
        private readonly TunnelInfo _tunnelInfo;

        public FakeCloudflaredService(TunnelInfo tunnelInfo)
        {
            _tunnelInfo = tunnelInfo;
        }

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> IsRunningAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<TunnelInfo> GetTunnelInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(_tunnelInfo);
        public Task<CommandResult> InstallAsync(string? operationId = null, CancellationToken cancellationToken = default) => Task.FromResult(new CommandResult());
        public Task<CommandResult> CreateTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default) => Task.FromResult(new CommandResult());
        public Task<CommandResult> StartAsync(string? operationId = null, CancellationToken cancellationToken = default) => Task.FromResult(new CommandResult());
        public Task<CommandResult> RestartAsync(string? operationId = null, CancellationToken cancellationToken = default) => Task.FromResult(new CommandResult());
        public Task<CommandResult> DeleteTunnelAsync(string? operationId = null, CancellationToken cancellationToken = default) => Task.FromResult(new CommandResult());
    }

    private sealed class FakeEndpointService : IEndpointService
    {
        public Task<IReadOnlyList<ServiceEndpoint>> GetEndpointsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ServiceEndpoint>>(Array.Empty<ServiceEndpoint>());
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public string Contents { get; private set; }

        public FakeFileSystem(string contents)
        {
            Contents = contents;
        }

        public string Combine(params string[] paths) => string.Join("/", paths);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => true;
        public void DeleteFile(string path) { }
        public bool DirectoryExists(string path) => false;
        public void CreateDirectory(string path) { }
        public void DeleteDirectory(string path, bool recursive) { }
        public void MoveDirectory(string sourcePath, string destinationPath) { }
        public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite) { }
        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) => Array.Empty<string>();
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            Contents = Encoding.UTF8.GetString(bytes);
            return Task.CompletedTask;
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Contents);
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult { ExitCode = 0 });
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "") => new();
    }
}
