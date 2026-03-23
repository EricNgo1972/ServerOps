using ServerOps.Application.DTOs;

namespace ServerOps.Application.Abstractions;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default);
}
