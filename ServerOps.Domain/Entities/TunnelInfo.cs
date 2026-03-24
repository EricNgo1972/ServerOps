namespace ServerOps.Domain.Entities;

public sealed record TunnelInfo
{
    public string TunnelId { get; init; } = string.Empty;
    public string TunnelName { get; init; } = string.Empty;
    public bool IsInstalled { get; init; }
    public bool IsServiceInstalled { get; init; }
    public bool IsConfigured { get; init; }
    public bool IsRemotelyManaged { get; init; }
    public bool IsRunning { get; init; }
    public string? ConfigPath { get; init; }
    public IReadOnlyList<string> IngressRules { get; init; } = Array.Empty<string>();
}
