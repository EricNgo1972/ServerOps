using System.Text;
using Microsoft.Extensions.Options;
using ServerOps.Application.Abstractions;
using ServerOps.Infrastructure.Configuration;

namespace ServerOps.Infrastructure.Deployment;

public sealed class DefaultDomainNameBuilder : IDomainNameBuilder
{
    private readonly IOptions<DomainOptions> _domainOptions;

    public DefaultDomainNameBuilder(IOptions<DomainOptions> domainOptions)
    {
        _domainOptions = domainOptions;
    }

    public string Build(string appName)
    {
        var suffix = _domainOptions.Value.DefaultDomainSuffix?.Trim().ToLowerInvariant() ?? string.Empty;
        var sanitizedAppName = Sanitize(appName);

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return sanitizedAppName;
        }

        return $"{sanitizedAppName}.{suffix}";
    }

    private static string Sanitize(string appName)
    {
        var input = (appName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(input))
        {
            return "app";
        }

        var builder = new StringBuilder(input.Length);
        var previousWasDash = false;

        foreach (var character in input)
        {
            if (char.IsLetterOrDigit(character) || character == '-')
            {
                builder.Append(character);
                previousWasDash = character == '-';
            }
            else if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "app" : sanitized;
    }
}
