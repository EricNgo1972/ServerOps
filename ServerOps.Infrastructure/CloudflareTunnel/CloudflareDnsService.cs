using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.CloudflareTunnel;

public sealed class CloudflareDnsService : ICloudflareDnsService
{
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";
    private readonly HttpClient _httpClient;
    private readonly IOptions<CloudflareOptions> _options;

    public CloudflareDnsService(HttpClient httpClient, IOptions<CloudflareOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task EnsureCNameAsync(string hostname, string target, CancellationToken ct = default)
    {
        var token = GetApiToken();
        var zoneId = GetZoneId();
        var existingRecords = await GetCNameRecordsAsync(hostname, token, zoneId, ct);
        var matchingRecord = existingRecords.FirstOrDefault(item =>
            string.Equals(item.Name, hostname, StringComparison.OrdinalIgnoreCase));

        if (matchingRecord is not null)
        {
            if (string.Equals(matchingRecord.Content, target, StringComparison.OrdinalIgnoreCase) &&
                matchingRecord.Proxied)
            {
                return;
            }

            using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/zones/{zoneId}/dns_records/{matchingRecord.Id}");
            updateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            updateRequest.Content = CreateCNameContent(hostname, target);

            using var updateResponse = await _httpClient.SendAsync(updateRequest, ct);
            updateResponse.EnsureSuccessStatusCode();
            return;
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/zones/{zoneId}/dns_records");
        createRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        createRequest.Content = CreateCNameContent(hostname, target);

        using var createResponse = await _httpClient.SendAsync(createRequest, ct);
        createResponse.EnsureSuccessStatusCode();
    }

    public async Task<bool> ExistsAsync(string hostname, CancellationToken ct = default)
    {
        return await ExistsAsync(hostname, null, ct);
    }

    private async Task<bool> ExistsAsync(string hostname, string? target, CancellationToken ct)
    {
        var token = GetApiToken();
        var zoneId = GetZoneId();
        var records = await GetCNameRecordsAsync(hostname, token, zoneId, ct);

        foreach (var item in records)
        {
            var hostnameMatches = string.Equals(item.Name, hostname, StringComparison.OrdinalIgnoreCase);
            var targetMatches = string.IsNullOrWhiteSpace(target)
                || string.Equals(item.Content, target, StringComparison.OrdinalIgnoreCase);

            if (hostnameMatches && targetMatches)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<CNameRecord>> GetCNameRecordsAsync(string hostname, string token, string zoneId, CancellationToken ct)
    {
        var encodedHostname = Uri.EscapeDataString(hostname);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/zones/{zoneId}/dns_records?type=CNAME&name={encodedHostname}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CNameRecord>();
        }

        var records = new List<CNameRecord>();
        foreach (var item in result.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? string.Empty : string.Empty;
            var name = item.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? string.Empty : string.Empty;
            var content = item.TryGetProperty("content", out var contentProperty) ? contentProperty.GetString() ?? string.Empty : string.Empty;
            var proxied = item.TryGetProperty("proxied", out var proxiedProperty) && proxiedProperty.ValueKind == JsonValueKind.True;

            records.Add(new CNameRecord
            {
                Id = id,
                Name = name,
                Content = content,
                Proxied = proxied
            });
        }

        return records;
    }

    public async Task DeleteAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return;
        }

        var token = GetApiToken();
        var zoneId = GetZoneId();
        var encodedHostname = Uri.EscapeDataString(hostname);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/zones/{zoneId}/dns_records?type=CNAME&name={encodedHostname}");
        listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var listResponse = await _httpClient.SendAsync(listRequest, ct);
        listResponse.EnsureSuccessStatusCode();

        await using var stream = await listResponse.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProperty))
            {
                continue;
            }

            var id = idProperty.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{BaseUrl}/zones/{zoneId}/dns_records/{id}");
            deleteRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var deleteResponse = await _httpClient.SendAsync(deleteRequest, ct);
            deleteResponse.EnsureSuccessStatusCode();
        }
    }

    private string GetApiToken()
    {
        var value = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = _options.Value.ApiToken;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Cloudflare API token is required.");
        }

        return value;
    }

    private string GetZoneId()
    {
        var value = Environment.GetEnvironmentVariable("CLOUDFLARE_ZONE_ID");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = _options.Value.ZoneId;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Cloudflare zone id is required.");
        }

        return value;
    }

    private static StringContent CreateCNameContent(string hostname, string target)
    {
        return new StringContent(
            JsonSerializer.Serialize(new
            {
                type = "CNAME",
                name = hostname,
                content = target,
                proxied = true
            }),
            Encoding.UTF8,
            "application/json");
    }

    private sealed class CNameRecord
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public bool Proxied { get; init; }
    }
}
