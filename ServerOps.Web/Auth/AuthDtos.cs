using System.Text.Json.Serialization;

namespace ServerOps.Web.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public bool RequiresTenantSelection { get; set; }
    public string? LoginToken { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    public LoginUser? User { get; set; }
    public LoginTenant? Tenant { get; set; }
}

public sealed class LoginUser
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PlatformRole { get; set; } = string.Empty;
}

public sealed class LoginTenant
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
