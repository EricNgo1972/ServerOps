using Microsoft.AspNetCore.SignalR;

namespace ServerOps.Web.Hubs;

public sealed class OperationLogHub : Hub
{
    public Task JoinOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return Task.CompletedTask;
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, operationId.Trim());
    }

    public Task LeaveOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return Task.CompletedTask;
        }

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, operationId.Trim());
    }
}
