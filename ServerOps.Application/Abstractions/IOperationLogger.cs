namespace ServerOps.Application.Abstractions;

public interface IOperationLogger
{
    Task LogAsync(string operationId, string stage, string message, CancellationToken ct = default);
}
