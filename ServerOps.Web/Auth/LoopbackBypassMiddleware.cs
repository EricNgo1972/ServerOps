using System.Security.Claims;

namespace ServerOps.Web.Auth;

public sealed class LoopbackBypassMiddleware
{
    private readonly RequestDelegate _next;

    public LoopbackBypassMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!(context.User.Identity?.IsAuthenticated ?? false) && IsLocalhostRequest(context))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "localhost"),
                new Claim(ClaimTypes.Email, "localhost"),
                new Claim(ClaimTypes.Role, "Administrator"),
                new Claim("bypass", "localhost")
            };

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "LoopbackBypass"));
        }

        await _next(context);
    }

    private static bool IsLocalhostRequest(HttpContext context)
    {
        var host = context.Request.Host.Host?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
    }
}
