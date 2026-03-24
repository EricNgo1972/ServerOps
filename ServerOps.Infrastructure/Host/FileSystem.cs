using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Host;

public sealed class FileSystem : IFileSystem
{
    public string Combine(params string[] paths) => Path.Combine(paths);

    public string GetTempPath() => Path.GetTempPath();

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        Directory.Move(sourcePath, destinationPath);
    }

    public void CopyDirectory(string sourcePath, string destinationPath, bool overwrite)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!source.Exists)
        {
            return;
        }

        Directory.CreateDirectory(destinationPath);

        foreach (var file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationPath, file.Name), overwrite);
        }

        foreach (var directory in source.GetDirectories())
        {
            CopyDirectory(directory.FullName, Path.Combine(destinationPath, directory.Name), overwrite);
        }
    }

    public IReadOnlyList<string> GetDirectories(string path)
    {
        if (!Directory.Exists(path))
        {
            return Array.Empty<string>();
        }

        return Directory.GetDirectories(path);
    }

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive)
    {
        if (!Directory.Exists(path))
        {
            return Array.Empty<string>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.GetFiles(path, searchPattern, option);
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        => File.WriteAllBytesAsync(path, bytes, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(path, cancellationToken);
}
