namespace ServerOps.Application.Abstractions;

public interface IDomainNameBuilder
{
    string Build(string appName);
}
