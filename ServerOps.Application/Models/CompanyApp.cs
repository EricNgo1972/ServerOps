namespace ServerOps.Application.Models;

public sealed record CompanyApp
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? RepoUrl { get; init; }
}
