using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ServerOps.Web.Auth;

public sealed class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient, IOptions<AuthServerOptions> options)
    {
        _httpClient = httpClient;
        var baseUrl = options.Value.BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute);
        }
    }

    public async Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var details = await ReadFailureAsync(response, ct);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? "Login failed." : details);
        }

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
        return payload;
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/forgot-password", new ForgotPasswordRequest
        {
            Email = email
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var details = await ReadFailureAsync(response, ct);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? "Unable to submit forgot password request." : details);
        }
    }

    private static async Task<string> ReadFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        }

        return content.Trim();
    }
}
