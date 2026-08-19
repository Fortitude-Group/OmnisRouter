using OmnisRouter.Adapters.OpenAI;
using OmnisRouter.Api.Auth;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.Api.Middleware;
using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Routing;
using OmnisRouter.Store;
using OmnisRouter.Store.Pricing;
using OmnisRouter.Telemetry;
using OmnisRouter.Upstream.Providers;
using OmnisRouter.Upstream.Security;

var builder = WebApplication.CreateBuilder(args);

// Foundation services (T016). Each extension owns its own registrations —
// AddOmnisStore also registers OmnisRouterDbContext + IDecisionLog.
builder.Services.AddOmnisByok();
builder.Services.AddOmnisStore(builder.Configuration);
builder.Services.AddOmnisPricing(o =>
    o.PricingDirectory = RepoLocator.Resolve(Path.Combine("config", "pricing")));
builder.AddOmnisTelemetry();

// US1: routing pipeline — model catalog + embedder + routing model + cluster-scorer policy.
builder.Services.AddOmnisRouting(builder.Configuration);

// Client-format adapters (ingress/egress). OpenAI in v1 MVP.
builder.Services.AddSingleton<IFormatAdapter, OpenAiAdapter>();

// Upstream provider clients (chosen-provider wire I/O). OpenAI in v1 MVP.
builder.Services.AddOmnisOpenAiUpstream();

// BYOK credential resolution for the chosen provider.
builder.Services.AddScoped<IProviderCredentialResolver, ProviderCredentialResolver>();

var app = builder.Build();

// Outermost first: the error boundary must wrap everything downstream of it, including auth.
app.UseOmnisErrorHandling();
app.UseRouterTokenAuth();

app.MapOmnisHealthEndpoints();
app.MapChatCompletions();
app.MapRoute();
app.MapModels();
app.MapAnalyticsDecisions();

app.Run();

// Test-visible partial so WebApplicationFactory<Program> can bootstrap the host in-process.
public partial class Program
{
}
