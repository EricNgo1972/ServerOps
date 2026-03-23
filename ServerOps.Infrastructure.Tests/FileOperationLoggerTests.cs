using System.Text;
using ServerOps.Application.Abstractions;
using ServerOps.Infrastructure.Deployment;
using Xunit;

namespace ServerOps.Infrastructure.Tests;

public sealed class FileOperationLoggerTests
{
    [Fact]
    public async Task LogAsync_Writes_File()
    {
        var fileSystem = new FakeFileSystem();
        var logger = new FileOperationLogger(fileSystem, new FakeRuntimeEnvironment());

        await logger.LogAsync("op-1", "Download", "Started");

        Assert.True(fileSystem.FileExists("/apps/_logs/op-1.log"));
        var contents = await fileSystem.ReadAllTextAsync("/apps/_logs/op-1.log");
        Assert.Contains("[Download] Started", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogAsync_Appends_Multiple_Lines()
    {
        var fileSystem = new FakeFileSystem();
        var logger = new FileOperationLogger(fileSystem, new FakeRuntimeEnvironment());

        await logger.LogAsync("op-1", "Download", "Started");
        await logger.LogAsync("op-1", "Extract", "Completed");

        var contents = await fileSystem.ReadAllTextAsync("/apps/_logs/op-1.log");
        Assert.Contains("[Download] Started", contents, StringComparison.Ordinal);
        Assert.Contains("[Extract] Completed", contents, StringComparison.Ordinal);
    }

    private sealed class FakeRuntimeEnvironment : IRuntimeEnvironment
    {
        public ServerOps.Domain.Enums.OsType GetCurrentOs() => ServerOps.Domain.Enums.OsType.Linux;
        public string GetAppsRootPath() => "/apps";
        public string GetCloudflaredConfigPath() => "/etc/cloudflared/config.yml";
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public string Combine(params string[] paths) => string.Join("/", paths).Replace("//", "/", StringComparison.Ordinal);
        public string GetTempPath() => "/tmp";
        public bool FileExists(string path) => _files.ContainsKey(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public void CreateDirectory(string path)
        {
            EnsureParents(path);
            _directories.Add(path);
        }
        public void DeleteDirectory(string path, bool recursive) => _directories.Remove(path);
        public void MoveDirectory(string sourcePath, string destinationPath) { }
        public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite) { }
        public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) => Array.Empty<string>();
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            EnsureParents(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/");
            _files[path] = bytes;
            return Task.CompletedTask;
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_files.TryGetValue(path, out var bytes) ? Encoding.UTF8.GetString(bytes) : string.Empty);

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
}
