using ServerOps.Infrastructure.Configuration;
using ServerOps.Web.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapPost("/api/deploy", DeployApiEndpoint.HandleAsync);

app.MapRazorComponents<ServerOps.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
