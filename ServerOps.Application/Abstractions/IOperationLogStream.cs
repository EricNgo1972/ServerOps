using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IOperationLogStream
{
    Task PublishAsync(OperationLogEvent logEvent, CancellationToken ct = default);
}
