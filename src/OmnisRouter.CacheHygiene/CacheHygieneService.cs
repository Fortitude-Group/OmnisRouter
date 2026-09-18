using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// The request-path facade: holds the analyzer, the in-memory lineage cache, and the options, and
/// analyses a completed request against its lineage. Fail-open by construction (spec FR-019): every
/// entry point swallows exceptions and returns null, so nothing here can fail, block, or slow a
/// request. Registered as a singleton (the lineage cache is shared, per-process state).
/// </summary>
public sealed class CacheHygieneService
{
    private readonly CacheHygieneAnalyzer _analyzer;
    private readonly LineageCache _lineage;
    private readonly CacheHygieneOptions _options;
    private readonly IReadOnlyList<INormalizer> _normalizers;

    public CacheHygieneService(IPricingBook pricing, CacheHygieneOptions options)
    {
        _options = options;
        _analyzer = new CacheHygieneAnalyzer(pricing);
        _lineage = new LineageCache(options.MaxLineageEntries, options.MaxLineageAge);
        _normalizers =
        [
            new LineEndingNormalizer(),
            new TrailingWhitespaceNormalizer(),
            new ToolOrderingNormalizer(),
        ];
    }

    public bool MeasurementEnabled => _options.MeasurementEnabled;

    /// <summary>
    /// Apply the enabled fixes to a request before it is forwarded, returning the request to send and
    /// which fix classes actually changed it. Off-by-default fixes are skipped, and a normaliser that
    /// cannot prove a request safe leaves it unchanged. Fail-open: never throws, so it can never break
    /// dispatch — on any error the original request is forwarded.
    /// </summary>
    public (ChatRequest Request, IReadOnlySet<FixClass> Applied) Normalise(ChatRequest request)
    {
        var applied = new HashSet<FixClass>();
        try
        {
            var current = request;
            foreach (var normalizer in _normalizers)
            {
                if (_options.IsFixEnabled(normalizer.Class) && normalizer.TryNormalize(current, out var next))
                {
                    current = next;
                    applied.Add(normalizer.Class);
                }
            }

            return (current, applied);
        }
        catch
        {
            return (request, applied);   // fail-open: a fix never costs a request
        }
    }

    /// <summary>
    /// Measure a completed request against its lineage, using the real usage. Records the current
    /// prefix for the next comparison. Returns the result when there is a miss worth reporting, else
    /// null. Never throws (fail-open) — a routed request is served whether or not this succeeds.
    /// </summary>
    public CacheHygieneResult? Analyse(
        ChatRequest raw, ChatRequest sent, IReadOnlySet<FixClass> appliedFixes, ModelRef model, Usage usage)
    {
        try
        {
            if (!_options.MeasurementEnabled)
            {
                return null;
            }

            var rawPrefix = PrefixExtractor.Extract(raw);
            if (rawPrefix is null)
            {
                return null;   // request is not using caching — nothing to measure
            }

            // What the provider actually cached is the prefix we sent (normalised if a fix ran).
            var sentPrefix = appliedFixes.Count > 0 ? PrefixExtractor.Extract(sent) ?? rawPrefix : rawPrefix;
            var normalised = appliedFixes.Count > 0 ? sentPrefix : null;

            var key = PrefixExtractor.LineageKey(raw);
            byte[]? previous = _lineage.TryGetPrevious(key, out var prev) ? prev : null;

            var result = _analyzer.Analyse(rawPrefix, normalised, previous, appliedFixes, usage, model, BillingModel.PayAsYouGo);

            _lineage.Store(key, sentPrefix);
            return result.Missed ? result : null;
        }
        catch
        {
            return null;   // fail-open: cache hygiene never costs a request
        }
    }
}
