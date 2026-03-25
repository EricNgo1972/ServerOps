using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using ServerOps.Web.Auth;
using ServerOps.Web.Api;
using ServerOps.Infrastructure.Configuration;
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.Sources.Add(new AzureTableConfigurationSource());

builder.Services.Configure<AuthServerOptions>(builder.Configuration.GetSection("AuthServer"));
builder.Services.AddHttpClient<AuthApiClient>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddSingleton<ServerOps.Web.Services.LiveOperationLogState>();
builder.Services.AddSingleton<ServerOps.Application.Abstractions.IOperationLogStream, ServerOps.Web.Services.SignalROperationLogStream>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseMiddleware<LoopbackBypassMiddleware>();
app.UseAuthorization();

app.MapPost("/api/deploy", DeployApiEndpoint.HandleAsync).RequireAuthorization();
app.MapPost("/auth/login", AuthApiEndpoint.LoginAsync);
app.MapPost("/auth/forgot-password", AuthApiEndpoint.ForgotPasswordAsync);
app.MapPost("/auth/logout", (Delegate)AuthApiEndpoint.LogoutAsync);
app.MapHub<ServerOps.Web.Hubs.OperationLogHub>("/hubs/operation-log").RequireAuthorization();

app.MapRazorComponents<ServerOps.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
