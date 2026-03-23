namespace ServerOps.Application.Abstractions;

public interface ILogService
{
    Task<IReadOnlyList<string>> GetLogLinesAsync(string operationId, int? maxLines = 500, CancellationToken ct = default);
    Task<bool> ExistsAsync(string operationId, CancellationToken ct = default);
}
