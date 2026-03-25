using Microsoft.Extensions.Configuration;

namespace ServerOps.Infrastructure.Configuration;

public sealed class AzureTableConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new AzureTableConfigurationProvider();
    }
}
