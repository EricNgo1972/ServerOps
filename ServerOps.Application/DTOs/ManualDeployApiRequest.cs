namespace ServerOps.Application.DTOs;

public sealed class ManualDeployApiRequest
{
    public string AppName { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
    public string? Hostname { get; set; }
}
