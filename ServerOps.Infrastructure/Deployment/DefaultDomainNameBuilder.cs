using System.Text;
using ServerOps.Application.Abstractions;

namespace ServerOps.Infrastructure.Deployment;

public sealed class DefaultDomainNameBuilder : IDomainNameBuilder
{
    public string Build(string label, string domainSuffix)
    {
        var suffix = domainSuffix?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new InvalidOperationException("Domain suffix is required.");
        }

        var sanitizedLabel = SanitizeLabel(label);
        if (string.IsNullOrWhiteSpace(sanitizedLabel))
        {
            throw new ArgumentException("Hostname label is required.", nameof(label));
        }

        return $"{sanitizedLabel}.{suffix}";
    }

    public string SanitizeLabel(string value)
    {
        var input = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
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
        return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
    }
}
