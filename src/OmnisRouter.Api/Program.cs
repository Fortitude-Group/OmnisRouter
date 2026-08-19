using OmnisRouter.Adapters.Anthropic;
using OmnisRouter.Adapters.Gemini;
using OmnisRouter.Adapters.OpenAI;
using OmnisRouter.Api.Auth;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.Api.Middleware;
using OmnisRouter.Api.Routing;
using Microsoft.EntityFrameworkCore;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Routing;
using OmnisRouter.Routing.Guardrails;
using OmnisRouter.Store;
using OmnisRouter.Store.Entities;
using OmnisRouter.Store.Pricing;
using OmnisRouter.Telemetry;
using OmnisRouter.Telemetry.Redaction;
using OmnisRouter.Upstream.Providers;
using OmnisRouter.Upstream.Security;

var builder = WebApplication.CreateBuilder(args);

// Foundation services. Each extension owns its own registrations —
// AddOmnisStore also registers OmnisRouterDbContext + IDecisionLog.
builder.Services.AddOmnisByok(o =>
{
    var keyPath = builder.Configuration["Byok:MasterKeyPath"];
    if (!string.IsNullOrWhiteSpace(keyPath))
    {
        o.KeyFilePath = keyPath;
    }
});
builder.Services.AddOmnisStore(builder.Configuration);
builder.Services.AddOmnisPricing(o =>
    o.PricingDirectory = RepoLocator.Resolve(Path.Combine("config", "pricing")));
builder.AddOmnisTelemetry();

// Belt-and-suspenders: scrub any accidental secret/prompt from log output (FR-014).
builder.Services.AddOmnisRedaction();

// Routing pipeline — model catalog + embedder + routing model + cluster-scorer policy.
builder.Services.AddOmnisRouting(builder.Configuration);

// Capability guardrails + session pinning (US4).
builder.Services.AddOmnisRoutingGuards();

// Client-format adapters (ingress/egress) — all three formats.
builder.Services.AddSingleton<IFormatAdapter, OpenAiAdapter>();
builder.Services.AddSingleton<IFormatAdapter, AnthropicAdapter>();
builder.Services.AddSingleton<IFormatAdapter, GeminiAdapter>();

// Upstream provider clients (chosen-provider wire I/O).
builder.Services.AddOmnisOpenAiUpstream();
builder.Services.AddOmnisAnthropicUpstream();
builder.Services.AddOmnisGeminiUpstream();
builder.Services.AddOmnisOpenRouterUpstream();

// Router-side image fetch+inline for providers that can't dereference a remote image URL.
builder.Services.AddHttpClient<IImageMaterializer, ImageMaterializer>();

// BYOK credential resolution for the chosen provider.
builder.Services.AddScoped<IProviderCredentialResolver, ProviderCredentialResolver>();

var app = builder.Build();

// Self-host bootstrap: create/upgrade the schema on start (no manual migration step), and seed the
// first router token from config when the store has none (solves the auth chicken-and-egg).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
    db.Database.Migrate();

    var bootstrapToken = app.Configuration["Omnis:BootstrapToken"];
    if (!string.IsNullOrWhiteSpace(bootstrapToken) && !db.RouterTokens.Any())
    {
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash(bootstrapToken),
            Name = "bootstrap",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }
}

// Outermost first: the error boundary must wrap everything downstream of it, including auth.
app.UseOmnisErrorHandling();
app.UseRouterTokenAuth();

app.MapOmnisHealthEndpoints();
app.MapChatCompletions();
app.MapMessages();
app.MapGeminiGenerate();
app.MapRoute();
app.MapModels();
app.MapAnalyticsDecisions();
app.MapKeys();

app.Run();

// Test-visible partial so WebApplicationFactory<Program> can bootstrap the host in-process.
public partial class Program
{
}
