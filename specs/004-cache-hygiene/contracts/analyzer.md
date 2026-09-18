# Contract: CacheHygieneAnalyzer + LineageCache

The internal surface of `OmnisRouter.CacheHygiene`. Signatures are indicative.

## CacheHygieneAnalyzer

```csharp
public sealed class CacheHygieneAnalyzer
{
    public CacheHygieneAnalyzer(IPricingBook pricing, IClock clock);

    // Pure: same inputs -> same result. No I/O, no Console, no network.
    CacheHygieneResult Analyse(
        ReadOnlySpan<byte> currentPrefixRaw,          // the wire prefix the caller sent (pre-fix)
        ReadOnlySpan<byte>? currentPrefixNormalised,  // the prefix after an enabled fix, if one ran
        ReadOnlySpan<byte>? previousPrefix,           // the lineage's last prefix, null if first-in-lineage
        Usage usage,                                  // real provider usage (cache read/creation tokens)
        ModelRef model,
        BillingModel billing);                        // payg | subscription (drives shadow_price)
}
```

**Guarantees**:
- Deterministic given its arguments; `IClock`/pricing injected so the £ stamp and any time field are
  testable.
- Divergence: the first differing byte between `currentPrefixRaw` and `previousPrefix`; the cause is
  classified from the bytes around it (research D7). Null `previousPrefix` → `Missed=false`, no figure.
- Measured: `RecomputedTokens` derives from the real `Usage` (cache-creation tokens), not a prediction.
- Saving: when `currentPrefixNormalised` matches `previousPrefix` but `currentPrefixRaw` would not, the
  fix turned a write into a read; `SavedTokens` = the tokens that would have recomputed, priced at
  (write − read).
- `WasteGbp` is 0 for an unavoidable cause.
- Conforms to the sibling RESULT contract version; a conformance test runs the sibling's vectors.

## LineageCache

```csharp
public sealed class LineageCache
{
    public LineageCache(int maxEntries, TimeSpan maxAge, IClock clock);
    bool TryGetPrevious(string lineageKey, out PrefixRef previous);
    void Store(string lineageKey, PrefixRef current);   // LRU + age evicted; never persisted
}
```

Bounded, in-memory, thread-safe. `lineageKey` = the session-pin identity, else a stable-structure hash
(research D4).

## Consumers

- **`RoutedRequestHandler`** — before dispatch, applies enabled normalisers to the neutral request
  (capturing before/after) within the budget; after the response, off the hot path, calls `Analyse`
  with the real usage and the lineage's previous prefix, then `Store`s the current prefix. Any exception
  in either step is swallowed; the request is served regardless (FR-019).
- **Receipt** — builds `ModelDecision.Cache` from the result.
- **`IngestRecordMapper` / `AnalyticsDecisions`** — emit the content-free block from the result.

## Fail-open

The analyzer and normalisers throw only into a boundary that swallows and serves the request. No member
blocks, retries, or performs network I/O.
