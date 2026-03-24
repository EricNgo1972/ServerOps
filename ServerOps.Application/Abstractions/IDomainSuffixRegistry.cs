namespace ServerOps.Application.Abstractions;

public interface IDomainSuffixRegistry
{
    Task<IReadOnlyList<string>> GetSuffixesAsync(CancellationToken ct = default);
}
