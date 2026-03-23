namespace ServerOps.Infrastructure.Configuration;

public sealed class PathsOptions
{
    public string LinuxAppsRoot { get; set; } = "/opt/serverops/apps";
    public string WindowsAppsRoot { get; set; } = @"C:\ServerOps\Apps";
    public string WindowsCloudflaredConfigPath { get; set; } = @"C:\ProgramData\cloudflared\config.yml";
}
