using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;

namespace ServerOps.Infrastructure.Deployment;

public sealed class NullOperationLogStream : IOperationLogStream
{
    public Task PublishAsync(OperationLogEvent logEvent, CancellationToken ct = default) => Task.CompletedTask;
}
