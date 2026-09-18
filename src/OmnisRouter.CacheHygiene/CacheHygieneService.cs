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

    public CacheHygieneService(IPricingBook pricing, CacheHygieneOptions options)
    {
        _options = options;
        _analyzer = new CacheHygieneAnalyzer(pricing);
        _lineage = new LineageCache(options.MaxLineageEntries, options.MaxLineageAge);
    }

    public bool MeasurementEnabled => _options.MeasurementEnabled;

    /// <summary>
    /// Measure a completed request against its lineage, using the real usage. Records the current
    /// prefix for the next comparison. Returns the result when there is a miss worth reporting, else
    /// null. Never throws (fail-open) — a routed request is served whether or not this succeeds.
    /// </summary>
    public CacheHygieneResult? Analyse(ChatRequest request, ModelRef model, Usage usage)
    {
        try
        {
            if (!_options.MeasurementEnabled)
            {
                return null;
            }

            var prefix = PrefixExtractor.Extract(request);
            if (prefix is null)
            {
                return null;   // request is not using caching — nothing to measure
            }

            var key = PrefixExtractor.LineageKey(request);
            byte[]? previous = _lineage.TryGetPrevious(key, out var prev) ? prev : null;

            // US1 measures only: no fix applied, no normalised prefix. US3 will pass those.
            var result = _analyzer.Analyse(prefix, currentPrefixNormalised: null, previous, fixApplied: null, usage, model, BillingModel.PayAsYouGo);

            _lineage.Store(key, prefix);
            return result.Missed ? result : null;
        }
        catch
        {
            return null;   // fail-open: cache hygiene never costs a request
        }
    }
}
