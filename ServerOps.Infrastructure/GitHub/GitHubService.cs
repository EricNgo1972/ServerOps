using System.Text.Json;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Domain.Entities;

namespace ServerOps.Infrastructure.GitHub;

public sealed class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;

    public GitHubService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GitHubReleaseQueryResult> GetReleasesAsync(string repo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return new GitHubReleaseQueryResult
            {
                Succeeded = false,
                ErrorMessage = "GitHub repository is required."
            };
        }

        var parts = repo.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return new GitHubReleaseQueryResult
            {
                Succeeded = false,
                ErrorMessage = $"Invalid GitHub repository format: '{repo}'."
            };
        }

        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = string.IsNullOrWhiteSpace(errorBody)
                ? $"GitHub request failed with status {(int)response.StatusCode} ({response.ReasonPhrase})."
                : $"GitHub request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {TrimError(errorBody)}";

            return new GitHubReleaseQueryResult
            {
                Succeeded = false,
                ErrorMessage = message
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var releases = document.RootElement.EnumerateArray()
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

        return new GitHubReleaseQueryResult
        {
            Succeeded = true,
            Releases = releases
        };
    }

    private static string TrimError(string value)
    {
        const int maxLength = 200;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
