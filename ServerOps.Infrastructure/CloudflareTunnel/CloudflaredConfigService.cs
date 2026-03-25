using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.CloudflareTunnel;

public sealed class CloudflaredConfigService : ICloudflaredConfigService
{
    private const string CloudflareApiBaseUrl = "https://api.cloudflare.com/client/v4";

    private readonly ICloudflaredService _cloudflaredService;
    private readonly IFileSystem _fileSystem;
    private readonly ICommandRunner _commandRunner;
    private readonly IEndpointService _endpointService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<CloudflareOptions> _cloudflareOptions;

    public CloudflaredConfigService(
        ICloudflaredService cloudflaredService,
        IFileSystem fileSystem,
        ICommandRunner commandRunner,
        IEndpointService endpointService,
        IHttpClientFactory httpClientFactory,
        IOptions<CloudflareOptions> cloudflareOptions)
    {
        _cloudflaredService = cloudflaredService;
        _fileSystem = fileSystem;
        _commandRunner = commandRunner;
        _endpointService = endpointService;
        _httpClientFactory = httpClientFactory;
        _cloudflareOptions = cloudflareOptions;
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
        if (tunnelInfo.IsRemotelyManaged)
        {
            await UpdateRemoteIngressAsync(tunnelInfo.TunnelId, hostname.Trim(), port, remove: false, ct);
            return;
        }

        await AddLocalIngressAsync(tunnelInfo, hostname.Trim(), port, ct);
    }

    public async Task RemoveIngressAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return;
        }

        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);
        if (tunnelInfo.IsRemotelyManaged)
        {
            await UpdateRemoteIngressAsync(tunnelInfo.TunnelId, hostname.Trim(), port: null, remove: true, ct);
            return;
        }

        await RemoveLocalIngressAsync(tunnelInfo, hostname.Trim(), ct);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var tunnelInfo = await _cloudflaredService.GetTunnelInfoAsync(ct);
        if (tunnelInfo.IsRemotelyManaged)
        {
            return;
        }

        var result = await _cloudflaredService.RestartAsync(cancellationToken: ct);

        if (!result.Succeeded)
        {
            var details = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                ? "Failed to reload cloudflared."
                : $"Failed to reload cloudflared. {details.Trim()}");
        }
    }

    private async Task AddLocalIngressAsync(TunnelInfo tunnelInfo, string hostname, int port, CancellationToken ct)
    {
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

        lines.Insert(insertIndex, $"    service: http://localhost:{port}");
        lines.Insert(insertIndex, $"  - hostname: {hostname}");

        var updatedContents = string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
        await _fileSystem.WriteAllBytesAsync(configPath, Encoding.UTF8.GetBytes(updatedContents), ct);
    }

    private async Task RemoveLocalIngressAsync(TunnelInfo tunnelInfo, string hostname, CancellationToken ct)
    {
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
        var hostnameLine = $"  - hostname: {hostname}".Trim();

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

    private async Task UpdateRemoteIngressAsync(string tunnelId, string hostname, int? port, bool remove, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tunnelId))
        {
            throw new InvalidOperationException("cloudflared tunnel id is not available.");
        }

        var endpoints = await _endpointService.GetEndpointsAsync(ct);
        var routes = endpoints
            .Where(endpoint => endpoint.IsExposed && !string.IsNullOrWhiteSpace(endpoint.Hostname) && endpoint.Port is not null)
            .ToDictionary(
                endpoint => endpoint.Hostname!,
                endpoint => endpoint.Port!.Value,
                StringComparer.OrdinalIgnoreCase);

        if (remove)
        {
            routes.Remove(hostname);
        }
        else if (port is int explicitPort)
        {
            routes[hostname] = explicitPort;
        }

        var ingress = routes
            .OrderBy(route => route.Key, StringComparer.OrdinalIgnoreCase)
            .Select(route => new Dictionary<string, string>
            {
                ["hostname"] = route.Key,
                ["service"] = $"http://localhost:{route.Value}"
            })
            .Cast<object>()
            .ToList();

        ingress.Add(new Dictionary<string, string>
        {
            ["service"] = "http_status:404"
        });

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetApiToken());

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{CloudflareApiBaseUrl}/accounts/{GetAccountId()}/cfd_tunnel/{tunnelId}/configurations");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                config = new
                {
                    ingress
                }
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private string GetApiToken()
    {
        var token = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _cloudflareOptions.Value.ApiToken;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Cloudflare API token is required.");
        }

        return token;
    }

    private string GetAccountId()
    {
        var accountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = _cloudflareOptions.Value.AccountId;
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("Cloudflare account id is required.");
        }

        return accountId;
    }
}
