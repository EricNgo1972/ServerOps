namespace ServerOps.Application.Abstractions;

public interface IExposureService
{
    Task ExposeAsync(string serviceName, string hostname, string? operationId = null, CancellationToken ct = default);
    Task UpdateAsync(string serviceName, string newHostname, string? operationId = null, CancellationToken ct = default);
    Task UnexposeAsync(string serviceName, string? operationId = null, CancellationToken ct = default);
}
