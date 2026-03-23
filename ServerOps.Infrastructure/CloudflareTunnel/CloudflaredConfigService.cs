using System.Text;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.CloudflareTunnel;

public sealed class CloudflaredConfigService : ICloudflaredConfigService
{
    private readonly ICloudflaredService _cloudflaredService;
    private readonly IFileSystem _fileSystem;
    private readonly ICommandRunner _commandRunner;

    public CloudflaredConfigService(
        ICloudflaredService cloudflaredService,
        IFileSystem fileSystem,
        ICommandRunner commandRunner)
    {
        _cloudflaredService = cloudflaredService;
        _fileSystem = fileSystem;
        _commandRunner = commandRunner;
    }

    public async Task AddIngressAsync(string hostname, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname is required.", nameof(hostname));
        }

        if (port <= 0)
        {
            throw new ArgumentException("Port must be greater than zero.", nameof(port));
        }

        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);
        if (string.IsNullOrWhiteSpace(tunnelInfo.ConfigPath))
        {
            throw new InvalidOperationException("cloudflared config path is not available.");
        }

        var configPath = tunnelInfo.ConfigPath;
        var contents = _fileSystem.FileExists(configPath)
            ? await _fileSystem.ReadAllTextAsync(configPath, ct)
            : string.Empty;

        if (contents.Contains($"hostname: {hostname}", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var lines = contents.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var insertIndex = lines.FindIndex(line => line.Contains("http_status:404", StringComparison.OrdinalIgnoreCase));
        if (insertIndex < 0)
        {
            throw new InvalidOperationException("cloudflared fallback ingress was not found.");
        }

        lines.Insert(insertIndex, $"  service: http://localhost:{port}");
        lines.Insert(insertIndex, $"- hostname: {hostname}");

        var updatedContents = string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
        await _fileSystem.WriteAllBytesAsync(configPath, Encoding.UTF8.GetBytes(updatedContents), ct);
    }

    public async Task RemoveIngressAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return;
        }

        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);
        if (string.IsNullOrWhiteSpace(tunnelInfo.ConfigPath))
        {
            throw new InvalidOperationException("cloudflared config path is not available.");
        }

        if (!_fileSystem.FileExists(tunnelInfo.ConfigPath))
        {
            return;
        }

        var configPath = tunnelInfo.ConfigPath;
        var contents = await _fileSystem.ReadAllTextAsync(configPath, ct);
        var lines = contents.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var updatedLines = new List<string>();
        var hostnameLine = $"- hostname: {hostname}".Trim();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.Equals(line.Trim(), hostnameLine, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < lines.Count && lines[i + 1].TrimStart().StartsWith("service:", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }

                continue;
            }

            updatedLines.Add(line);
        }

        var updatedContents = string.Join(Environment.NewLine, updatedLines).TrimEnd() + Environment.NewLine;
        await _fileSystem.WriteAllBytesAsync(configPath, Encoding.UTF8.GetBytes(updatedContents), ct);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var result = await _commandRunner.RunAsync(new CommandRequest
        {
            Command = "systemctl",
            Arguments = ["restart", "cloudflared"]
        }, ct);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Failed to reload cloudflared.");
        }
    }
}
