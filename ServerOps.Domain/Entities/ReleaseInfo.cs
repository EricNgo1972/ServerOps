namespace ServerOps.Domain.Entities;

public sealed record ReleaseInfo
{
    public string Tag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset PublishedAt { get; init; }
    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = Array.Empty<ReleaseAsset>();
}
