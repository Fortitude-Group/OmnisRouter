using OmnisRouter.Api.Auth;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.Api.Middleware;
using OmnisRouter.Store;
using OmnisRouter.Store.Pricing;
using OmnisRouter.Telemetry;
using OmnisRouter.Upstream.Security;

var builder = WebApplication.CreateBuilder(args);

// Foundation services (T016). Each extension owns its own registrations —
// AddOmnisStore also registers OmnisRouterDbContext + IDecisionLog.
builder.Services.AddOmnisByok();
builder.Services.AddOmnisStore(builder.Configuration);
builder.Services.AddOmnisPricing();
builder.AddOmnisTelemetry();

// US1: routing pipeline registered here (adapters, upstream request execution, routing model).

var app = builder.Build();

// Outermost first: the error boundary must wrap everything downstream of it, including auth.
app.UseOmnisErrorHandling();
app.UseRouterTokenAuth();

app.MapOmnisHealthEndpoints();

app.Run();

// Test-visible partial so WebApplicationFactory<Program> can bootstrap the host in-process.
public partial class Program
{
}
