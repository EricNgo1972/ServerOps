namespace ServerOps.Application.Abstractions;

public interface IHealthVerificationService
{
    Task<bool> VerifyAsync(string appName, CancellationToken ct = default);
}
