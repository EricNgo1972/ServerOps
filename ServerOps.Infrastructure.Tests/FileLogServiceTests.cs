using System.Text;
using ServerOps.Application.Abstractions;
using ServerOps.Infrastructure.Deployment;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class FileLogServiceTests
{
    [Fact]
    public async Task GetLogLinesAsync_Returns_Empty_When_File_Missing()
    {
        var service = new FileLogService(new FakeFileSystem(), new FakeRuntimeEnvironment());

        var lines = await service.GetLogLinesAsync("missing");

        Assert.Empty(lines);
    }

    [Fact]
    public async Task GetLogLinesAsync_Returns_Last_Max_Lines()
    {
        var fileSystem = new FakeFileSystem();
        await fileSystem.WriteAllBytesAsync("/apps/_logs/op-1.log", Encoding.UTF8.GetBytes("a\nb\nc\nd\n"));
        var service = new FileLogService(fileSystem, new FakeRuntimeEnvironment());

        var lines = await service.GetLogLinesAsync("op-1", 2);

        Assert.Equal(["c", "d"], lines);
    }

    private sealed class FakeRuntimeEnvironment : IRuntimeEnvironment
    {
        public ServerOps.Domain.Enums.OsType GetCurrentOs() => ServerOps.Domain.Enums.OsType.Linux;
        public string GetAppsRootPath() => "/apps";
        public string GetCloudflaredConfigPath() => "/etc/cloudflared/config.yml";
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => _files.ContainsKey(path);
        public bool DirectoryExists(string path) => false;
        public void CreateDirectory(string path) { }
        public void DeleteDirectory(string path, bool recursive) { }
        public void MoveDirectory(string sourcePath, string destinationPath) { }
        public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite) { }
        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) => Array.Empty<string>();
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            _files[path] = bytes;
            return Task.CompletedTask;
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_files.TryGetValue(path, out var bytes) ? Encoding.UTF8.GetString(bytes) : string.Empty);
    }
}
