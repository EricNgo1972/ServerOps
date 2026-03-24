using ServerOps.Web.Api;
using ServerOps.Infrastructure.Configuration;
var builder = WebApplication.CreateBuilder(args);

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

app.MapPost("/api/deploy", DeployApiEndpoint.HandleAsync);
app.MapHub<ServerOps.Web.Hubs.OperationLogHub>("/hubs/operation-log");

app.MapRazorComponents<ServerOps.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
