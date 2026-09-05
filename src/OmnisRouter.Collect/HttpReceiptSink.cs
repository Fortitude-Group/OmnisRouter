using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmnisRouter.Collect;

/// <summary>
/// Posts content-free receipts to OmnisVigil <c>/v1/ingest</c>. This is the current
/// <c>TranscriptCollector.PostBatchAsync</c> logic verbatim, moved behind <see cref="IReceiptSink"/>
/// so the engine is network-free under test and identical on the wire whether driven by the CLI or
/// the tray (FR-022).
/// </summary>
public sealed class HttpReceiptSink : IReceiptSink, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>Build a sink that owns its own client for <paramref name="endpoint"/> with a Bearer <paramref name="key"/>.</summary>
    public HttpReceiptSink(string endpoint, string key)
    {
        _http = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        _ownsClient = true;
    }

    /// <summary>Build a sink over a caller-supplied client (tests, shared handlers).</summary>
    public HttpReceiptSink(HttpClient http)
    {
        _http = http;
        _ownsClient = false;
    }

    public async Task<PostResult> PostAsync(IReadOnlyList<JsonObject> records, CancellationToken ct)
    {
        var array = new JsonArray();
        foreach (var r in records)
        {
            array.Add(r);   // each record is built fresh per batch and never reused, so no clone
        }

        var body = new JsonObject { ["schema_version"] = 1, ["records"] = array };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("/v1/ingest", content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ingest failed ({(int)response.StatusCode}): {text}");
        }

        using var doc = JsonDocument.Parse(text);
        return new PostResult(ReadInt(doc.RootElement, "accepted"), ReadInt(doc.RootElement, "duplicates"));

        static int ReadInt(JsonElement el, string name)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetInt32(out var n))
                {
                    return n;
                }
            }

            return 0;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
