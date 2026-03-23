namespace ServerOps.Domain.Entities;

public sealed record ReleaseAsset
{
    public string Name { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long Size { get; init; }
}
