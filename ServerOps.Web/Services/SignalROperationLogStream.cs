using Microsoft.AspNetCore.SignalR;
using ServerOps.Application.Abstractions;
using ServerOps.Application.DTOs;
using ServerOps.Web.Hubs;

namespace ServerOps.Web.Services;

public sealed class SignalROperationLogStream : IOperationLogStream
{
    private readonly IHubContext<OperationLogHub> _hubContext;

    public SignalROperationLogStream(IHubContext<OperationLogHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishAsync(OperationLogEvent logEvent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logEvent.OperationId))
        {
            return Task.CompletedTask;
        }

        return _hubContext.Clients.Group(logEvent.OperationId.Trim()).SendAsync("log", logEvent, ct);
    }
}
