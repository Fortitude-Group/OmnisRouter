# Internal Interfaces (module boundaries)

These are the internal contracts that keep the solution modular and independently testable (Constitution I & III). Signatures only — implementations belong in `tasks.md` / the implementation phase.

## Format adapters (`OmnisRouter.Adapters`)

```csharp
public interface IFormatAdapter
{
    string Name { get; } // "anthropic" | "openai" | "gemini"

    // Ingress: wire body -> neutral request
    ChatRequest ToInternal(JsonElement body, string? pathModel = null);

    // Egress to upstream: neutral request -> upstream HTTP request for a chosen model
    HttpRequestMessage FromInternal(ChatRequest req, ModelRef model);

    // Non-streaming response translation back to THIS adapter's client format
    JsonElement ToClientResponse(ChatResponse resp, ModelDecision receipt);

    // Streaming: re-frame neutral events into this client format's SSE shape (R2/R3)
    IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events, ModelDecision receipt, CancellationToken ct);
}
```
Conformance: golden-file round-trip + cross-format tests per adapter (Anthropic-in → OpenAI-model → Anthropic-out, etc.).

## Routing (`OmnisRouter.Routing`, `OmnisRouter.Core`)

```csharp
public interface IEmbedder            // ONNX bge-small-en-v1.5, in-process, no network
{
    int Dimension { get; }            // 384
    float[] Embed(string text);
}

public interface IRoutingPolicy
{
    string Name { get; }
    ModelDecision Decide(ChatRequest req, RoutingContext ctx);
}

// Default v1 implementation: ClusterScorerPolicy
//   embed -> nearest centroid (cosine) -> softmax confidence
//   -> policy-table lookup (cheapest capable within relative quality band)
//   -> confidence floor gate (escalate if below) -> capability guardrails -> ModelDecision

public interface ICapabilityGuard      // pre-dispatch guardrail checks (R2)
{
    // Returns null if OK to route; otherwise the guardrail refusal/notice.
    GuardResult Check(ChatRequest req, ModelRef candidate);
}

public interface ISessionPinner
{
    string ResolveKey(ChatRequest req, string tenantId);   // header else HMAC(secret, tenant‖system‖first-user)
    ModelRef? GetPin(string sessionKey, int clusterId);    // null if none / cluster changed
    void Pin(string sessionKey, ModelRef model, int clusterId);
}
```

## Upstream + BYOK (`OmnisRouter.Upstream`)

```csharp
public interface IUpstreamClient       // one per provider
{
    string Provider { get; }
    Task<ChatResponse> SendAsync(ChatRequest req, ModelRef model, ProviderCredential key, CancellationToken ct);
    IAsyncEnumerable<NeutralStreamEvent> StreamAsync(ChatRequest req, ModelRef model, ProviderCredential key, CancellationToken ct);
    // impl: SocketsHttpHandler + InfiniteTimeSpan + HttpCompletionOption.ResponseHeadersRead; no retry on the streaming path
}

public interface ISecretCipher
{
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default);
    byte[] Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData = default); // AES-256-GCM
}

public interface IMasterKeyProvider
{
    ReadOnlySpan<byte> GetCurrentKey(out int keyVersion);  // v1: LocalFileMasterKeyProvider; hosted: KmsMasterKeyProvider
    ReadOnlySpan<byte> GetKey(int keyVersion);
}
```

## Storage & pricing (`OmnisRouter.Store`)

```csharp
// EF Core; SQLite default / Npgsql optional; per-provider migrations assemblies.
// ProviderKey.ApiKeyEncrypted mapped via a ValueConverter closing over ISecretCipher at model-build time.
public interface IDecisionLog
{
    Task AppendAsync(DecisionLogEntry entry, CancellationToken ct);          // no prompt content / keys
    IAsyncEnumerable<DecisionLogEntry> ExportAsync(DecisionQuery q, CancellationToken ct);
}

public interface IPricingBook           // from config/pricing/<date>.yaml (pinned snapshot)
{
    string SnapshotDate { get; }
    decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens);
}
```

## Routing-model build (`OmnisRouter.RoutingModel.Build` — offline CLI)

```
build-model  --datasets <public-dataset-refs>
             --bench-results <omnisbench-run>
             --k 64
             --epsilon 0.05
             --min-samples 30
             --out routing/            # emits centroids-<ver>.bin + policy-<ver>.json + build manifest
```
Reproducible: same inputs → same versioned routing model (FR-006); `policy_version` is stamped into every decision.
