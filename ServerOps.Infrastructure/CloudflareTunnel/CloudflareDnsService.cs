using System.Text;
using System.Text.Json;
using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.CloudflareTunnel;

public sealed class CloudflareDnsService : ICloudflareDnsService
{
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";
    private readonly HttpClient _httpClient;

    public CloudflareDnsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task EnsureCNameAsync(string hostname, string target, CancellationToken ct = default)
    {
        if (await ExistsAsync(hostname, ct))
        {
            return;
        }

        var token = GetRequiredEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        var zoneId = GetRequiredEnvironmentVariable("CLOUDFLARE_ZONE_ID");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/zones/{zoneId}/dns_records");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                type = "CNAME",
                name = hostname,
                content = target
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> ExistsAsync(string hostname, CancellationToken ct = default)
    {
        var token = GetRequiredEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        var zoneId = GetRequiredEnvironmentVariable("CLOUDFLARE_ZONE_ID");

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
            return false;
        }

        return result.GetArrayLength() > 0;
    }

    public async Task DeleteAsync(string hostname, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return;
        }

        var token = GetRequiredEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        var zoneId = GetRequiredEnvironmentVariable("CLOUDFLARE_ZONE_ID");
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

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Environment variable '{name}' is required.");
        }

        return value;
    }
}
