namespace ServerOps.Application.Abstractions;

public interface IDomainNameBuilder
{
    string Build(string label, string domainSuffix);
    string SanitizeLabel(string value);
}
