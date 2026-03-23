namespace ServerOps.Domain.Entities;

public sealed record TunnelInfo
{
    public string TunnelId { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public string? ConfigPath { get; init; }
    public IReadOnlyList<string> IngressRules { get; init; } = Array.Empty<string>();
}
