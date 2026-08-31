using System.Net;
using System.Text.Json.Nodes;

namespace OmnisRouter.Vigil;

/// <summary>Result of a policy fetch: whether it changed, the new policy (when changed), and the ETag to send next time.</summary>
public sealed record PolicyFetch(bool Changed, VigilPolicy? Policy, string? ETag);

public interface IVigilPolicyClient
{
    Task<PolicyFetch> FetchAsync(string? etag, CancellationToken cancellationToken);
}

/// <summary>GETs GET /v1/policy with conditional If-None-Match, parsing the body into a <see cref="VigilPolicy"/>.</summary>
public sealed class HttpVigilPolicyClient : IVigilPolicyClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OmnisVigilOptions _options;

    public HttpVigilPolicyClient(IHttpClientFactory httpFactory, OmnisVigilOptions options)
    {
        _httpFactory = httpFactory;
        _options = options;
    }

    public async Task<PolicyFetch> FetchAsync(string? etag, CancellationToken cancellationToken)
    {
        var http = _httpFactory.CreateClient("OmnisVigil");
        using var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(_options.Endpoint!, "v1/policy"));
        request.Headers.Authorization = new("Bearer", _options.ProjectKey);
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new PolicyFetch(false, null, etag);
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var newEtag = response.Headers.ETag?.Tag ?? etag;
        return new PolicyFetch(true, Parse(json), newEtag);
    }

    internal static VigilPolicy Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("OmnisVigil policy body is not a JSON object.");

        var caps = root["caps"]?.AsObject();
        var kill = root["kill"]?.AsObject();

        return new VigilPolicy
        {
            PolicyVersion = root["policy_version"]?.GetValue<string>() ?? "",
            ConfidenceFloor = root["confidence_floor"] is { } cf ? cf.GetValue<double>() : null,
            AllowedModels = ReadStringArray(root["allowed_models"]),
            AllowedModelsByProject = ReadStringArrayMap(root["allowed_models_by_project"]),
            Caps = new PolicyCaps
            {
                MonthlyUsd = caps?["monthly_usd"] is { } m ? m.GetValue<decimal>() : null,
                PerProjectUsd = ReadDecimalMap(caps?["per_project_usd"]),
                SpentUsd = caps?["spent_usd"] is { } sp ? sp.GetValue<decimal>() : 0m,
                SpentPerProjectUsd = ReadDecimalMap(caps?["spent_per_project_usd"]),
            },
            Kill = new PolicyKill
            {
                Org = kill?["org"]?.GetValue<bool>() ?? false,
                Teams = ReadStringArray(kill?["teams"]),
            },
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray arr)
        {
            return [];
        }

        var list = new List<string>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not null)
            {
                list.Add(item.GetValue<string>());
            }
        }

        return list;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadStringArrayMap(JsonNode? node)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                map[key] = ReadStringArray(value);
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<string, decimal> ReadDecimalMap(JsonNode? node)
    {
        var map = new Dictionary<string, decimal>();
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                if (value is not null)
                {
                    map[key] = value.GetValue<decimal>();
                }
            }
        }

        return map;
    }

    private static string CombineUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
