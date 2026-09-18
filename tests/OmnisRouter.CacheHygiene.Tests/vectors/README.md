# Ported cache vectors (cross-tool conformance)

Byte-exact copies of ProseWeightVisualizer's CacheScope ground-truth fixtures
(`ProseWeightVisualizer/data/cache/fixtures/`), ported here (T003) so OmnisRouter's analyzer is held to
the same reference cases as the sibling tool that owns the CacheScope RESULT/CAPTURE contracts.

| File | What it plants |
|---|---|
| `crlf_prev.txt` / `lf_next.txt` | The same two lines differing only by a `\r` — the CRLF-drift case (the proven avoidable win). |
| `volatile_header.md` | A header carrying a volatile `updated:` date — a structural, unavoidable divergence. |
| `below_minimum.txt` | A prompt shorter than the minimum cacheable length — never cached. |

**Do not re-save these files** through an editor that rewrites line endings: the `\r` in `crlf_prev.txt`
is the entire point of the vector. They are copied to the test output verbatim (`PreserveNewest`).

`AnalyzerConformanceTests` asserts the cases OmnisRouter's analyzer conforms on today (CRLF drift).
Timestamp/volatile-header classification is a tracked refinement in `CacheHygieneAnalyzer.Classify`;
until it lands, a volatile-header-only divergence is reported conservatively as a genuine edit
(unavoidable, £0), which is safe — it never overstates avoidable waste.
