namespace ServerOps.Application.Abstractions;

public interface IExposureService
{
    Task ExposeAsync(string serviceName, string hostname, CancellationToken ct = default);
    Task UpdateAsync(string serviceName, string newHostname, CancellationToken ct = default);
    Task UnexposeAsync(string serviceName, CancellationToken ct = default);
}
