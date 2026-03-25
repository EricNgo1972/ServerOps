using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace ServerOps.Web.Auth;

public static class AuthApiEndpoint
{
    public static async Task<IResult> LoginAsync(
        HttpContext context,
        [FromBody] LoginRequest request,
        AuthApiClient authApiClient,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest("Email and password are required.");
        }

        try
        {
            var response = await authApiClient.LoginAsync(request.Email.Trim(), request.Password, ct);
            if (response is null || string.IsNullOrWhiteSpace(response.AccessToken) || response.User is null)
            {
                return Results.BadRequest("Login failed.");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, response.User.UserId ?? string.Empty),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(response.User.DisplayName) ? request.Email.Trim() : response.User.DisplayName),
                new(ClaimTypes.Email, response.User.Email ?? request.Email.Trim()),
                new(ClaimTypes.Role, string.IsNullOrWhiteSpace(response.User.PlatformRole) ? "User" : response.User.PlatformRole)
            };

            if (!string.IsNullOrWhiteSpace(response.Tenant?.TenantId))
            {
                claims.Add(new Claim("tenant_id", response.Tenant.TenantId));
            }

            if (response.AccessTokenExpiresAtUtc is { } expiresAtUtc)
            {
                claims.Add(new Claim("access_token_expires_at_utc", expiresAtUtc.ToString("O")));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = response.AccessTokenExpiresAtUtc
                });

            return Results.Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    public static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        AuthApiClient authApiClient,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest("Email is required.");
        }

        try
        {
            await authApiClient.ForgotPasswordAsync(request.Email.Trim(), ct);
            return Results.Ok(new { success = true, message = "If the account exists, password reset instructions have been sent." });
        }
        catch
        {
            return Results.Ok(new { success = true, message = "If the account exists, password reset instructions have been sent." });
        }
    }

    public static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { success = true });
    }
}
