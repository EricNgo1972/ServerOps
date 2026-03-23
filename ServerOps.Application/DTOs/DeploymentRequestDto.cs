namespace ServerOps.Application.DTOs;

public sealed class DeploymentRequestDto
{
    public string AppName { get; set; } = string.Empty;
    public string AssetUrl { get; set; } = string.Empty;
}
