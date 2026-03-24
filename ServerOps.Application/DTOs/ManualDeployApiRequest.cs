namespace ServerOps.Application.DTOs;

public sealed class ManualDeployApiRequest
{
    public string AppName { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
    public int? PortOverride { get; set; }
    public string? Hostname { get; set; }
    public string? DomainSuffix { get; set; }
}
