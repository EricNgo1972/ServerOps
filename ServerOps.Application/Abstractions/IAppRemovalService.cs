using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IAppRemovalService
{
    Task<CommandResult> RemoveAsync(string appName, string? operationId = null, CancellationToken ct = default);
}
