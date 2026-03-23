namespace ServerOps.Application.Abstractions;

public interface IFileSystem
{
    string Combine(params string[] paths);
    string GetTempPath();
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive);
    void MoveDirectory(string sourcePath, string destinationPath);
    void CopyDirectory(string sourcePath, string destinationPath, bool overwrite);
    IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
}
