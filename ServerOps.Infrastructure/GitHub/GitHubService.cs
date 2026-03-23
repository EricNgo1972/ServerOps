using System.Text.Json;
using ServerOps.Application.Abstractions;
using ServerOps.Domain.Entities;

namespace ServerOps.Infrastructure.GitHub;

public sealed class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;

    public GitHubService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(string repo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return [];
        }

        var parts = repo.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return [];
        }

        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.EnumerateArray()
            .Select(release => new ReleaseInfo
            {
                Tag = release.GetProperty("tag_name").GetString() ?? string.Empty,
                Name = release.GetProperty("name").GetString() ?? string.Empty,
                PublishedAt = release.TryGetProperty("published_at", out var publishedAt) && publishedAt.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.Parse(publishedAt.GetString()!)
                    : DateTimeOffset.MinValue,
                Assets = release.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseAsset
                {
                    Name = asset.GetProperty("name").GetString() ?? string.Empty,
                    DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    Size = asset.GetProperty("size").GetInt64()
                }).ToList()
            })
            .ToList();
    }
}
