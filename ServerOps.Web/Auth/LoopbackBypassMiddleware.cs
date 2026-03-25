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
        if (!(context.User.Identity?.IsAuthenticated ?? false) && IsLoopback(context))
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

    private static bool IsLoopback(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp);
    }
}
