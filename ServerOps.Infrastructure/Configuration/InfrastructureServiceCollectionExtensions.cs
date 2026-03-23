using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Application.Services;
using ServerOps.Infrastructure.CloudflareTunnel;
using ServerOps.Infrastructure.Deployment;
using ServerOps.Infrastructure.GitHub;
using ServerOps.Infrastructure.Host;
using ServerOps.Infrastructure.Networking;
using ServerOps.Infrastructure.Registry;

namespace ServerOps.Infrastructure.Configuration;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CloudflareOptions>(configuration.GetSection("Cloudflare"));
        services.Configure<GitHubOptions>(configuration.GetSection("GitHub"));
        services.Configure<PathsOptions>(configuration.GetSection("Paths"));

        services.AddMemoryCache();
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IArchiveService, ZipArchiveService>();
        services.AddSingleton<IRuntimeEnvironment, RuntimeEnvironment>();

        services.AddSingleton<LinuxHostService>();
        services.AddSingleton<WindowsHostService>();
        services.AddSingleton<LinuxPortService>();
        services.AddSingleton<WindowsPortService>();

        services.AddSingleton<IHostService, HostService>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IPortService, PortService>();
        services.AddSingleton<ICloudflaredService, CloudflaredService>();
        services.AddSingleton<ICloudflaredConfigService, CloudflaredConfigService>();
        services.AddSingleton<IDeploymentPackageValidator, DeploymentPackageValidator>();
        services.AddSingleton<IHealthVerificationService, HealthVerificationService>();
        services.AddSingleton<IDeploymentHistoryStore, JsonDeploymentHistoryStore>();
        services.AddSingleton<IDeploymentService, DeploymentService>();
        services.AddSingleton<IRollbackService, RollbackService>();
        services.AddSingleton<AzureTableAppRegistry>();
        services.AddSingleton<ICompanyAppRegistry, CachedCompanyAppRegistry>();
        services.AddSingleton<IManagedAppFilter, ManagedAppFilter>();
        services.AddSingleton<IAppTopologyService, AppTopologyService>();
        services.AddSingleton<IEndpointRegistry, InMemoryEndpointRegistry>();
        services.AddSingleton<IEndpointService, EndpointService>();
        services.AddSingleton<IExposureService, ExposureService>();
        services.AddSingleton<IAppCatalogService, AppCatalogService>();
        services.AddHttpClient<ICloudflareDnsService, CloudflareDnsService>();

        services.AddHttpClient<IGitHubService, GitHubService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ServerOps", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
            }
        });

        services.AddHttpClient();

        return services;
    }
}
