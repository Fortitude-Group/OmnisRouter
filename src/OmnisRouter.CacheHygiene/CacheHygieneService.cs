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
    private static readonly FixClass[] AllFixes = Enum.GetValues<FixClass>();

    private readonly CacheHygieneAnalyzer _analyzer;
    private readonly LineageCache _lineage;
    private readonly CacheHygieneOptions _options;
    private readonly IFixPolicy? _fixPolicy;
    private readonly CacheHygieneTally? _tally;
    private readonly IReadOnlyList<INormalizer> _normalizers;

    // The local enabled-fix set, swappable at runtime (from the tray). Volatile reference so a swap is
    // atomic and Normalise always reads a whole, consistent set. Policy still overrides this layer.
    private volatile IReadOnlySet<FixClass> _localEnabled;

    public CacheHygieneService(
        IPricingBook pricing, CacheHygieneOptions options, IFixPolicy? fixPolicy = null, CacheHygieneTally? tally = null)
    {
        _options = options;
        _fixPolicy = fixPolicy;
        _tally = tally;
        _localEnabled = new HashSet<FixClass>(options.EnabledFixes);
        _analyzer = new CacheHygieneAnalyzer(pricing);
        _lineage = new LineageCache(options.MaxLineageEntries, options.MaxLineageAge);
        _normalizers =
        [
            new LineEndingNormalizer(),
            new TrailingWhitespaceNormalizer(),
            new ToolOrderingNormalizer(),
        ];
    }

    /// <summary>
    /// Whether a fix runs: the control-plane policy decides when it has an opinion, otherwise the local
    /// config. This lets an OmnisVigil policy turn a fix on or off fleet-wide (FR-012) while a
    /// self-hosted router with no policy keeps its local, default-off behaviour.
    /// </summary>
    private bool IsFixEnabled(FixClass fix) => _fixPolicy?.IsFixEnabled(fix) ?? _localEnabled.Contains(fix);

    public bool MeasurementEnabled => _options.MeasurementEnabled;

    /// <summary>The fix classes enabled in local config/runtime (before any policy override).</summary>
    public IReadOnlyCollection<FixClass> LocalEnabledFixes => (IReadOnlyCollection<FixClass>)_localEnabled;

    /// <summary>What actually runs, after any OmnisVigil policy override.</summary>
    public IReadOnlyCollection<FixClass> EffectiveEnabledFixes() => Array.FindAll(AllFixes, IsFixEnabled);

    /// <summary>True when a control-plane policy has an opinion on the fixes, so it is authoritative over local.</summary>
    public bool PolicyOverridesFixes => _fixPolicy is not null && Array.Exists(AllFixes, f => _fixPolicy.IsFixEnabled(f) is not null);

    /// <summary>Swap the local enabled-fix set at runtime (atomic reference set). Policy precedence is unchanged.</summary>
    public void SetLocalEnabledFixes(IEnumerable<FixClass> fixes) => _localEnabled = new HashSet<FixClass>(fixes);

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
                if (IsFixEnabled(normalizer.Class) && normalizer.TryNormalize(current, out var next))
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

            var result = _analyzer.Analyse(rawPrefix, normalised, previous, appliedFixes, usage, model, _options.Billing);

            _lineage.Store(key, sentPrefix);
            if (result.Missed)
            {
                _tally?.Record(result);   // feed the running total the tray reads
                return result;
            }

            return null;
        }
        catch
        {
            return null;   // fail-open: cache hygiene never costs a request
        }
    }
}
