using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface IAppRemovalService
{
    Task<CommandResult> RemoveAsync(string appName, CancellationToken ct = default);
}
