namespace ServerOps.Application.Abstractions;

public interface ICloudflareDnsService
{
    Task EnsureCNameAsync(string hostname, string target, CancellationToken ct = default);
    Task<bool> ExistsAsync(string hostname, CancellationToken ct = default);
    Task DeleteAsync(string hostname, CancellationToken ct = default);
}
