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

        const string fallbackLine = "- service: http_status:404";
        var ingressBlock = $"- hostname: {hostname}{Environment.NewLine}  service: http://localhost:{port}{Environment.NewLine}";

        string updatedContents;
        var fallbackIndex = contents.IndexOf(fallbackLine, StringComparison.Ordinal);
        if (fallbackIndex >= 0)
        {
            updatedContents = contents.Insert(fallbackIndex, ingressBlock);
        }
        else
        {
            var prefix = string.IsNullOrWhiteSpace(contents) ? string.Empty : contents.TrimEnd() + Environment.NewLine;
            updatedContents = $"{prefix}{ingressBlock}{fallbackLine}{Environment.NewLine}";
        }

        await _fileSystem.WriteAllBytesAsync(configPath, Encoding.UTF8.GetBytes(updatedContents), ct);
    }

    public async Task RemoveIngressAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return;
        }

        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);
        if (string.IsNullOrWhiteSpace(tunnelInfo.ConfigPath) || !_fileSystem.FileExists(tunnelInfo.ConfigPath))
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
