# Contract: Normalizers (the opt-in fixes)

Three fix classes, each a transform on the neutral request applied at the egress wire serialisation,
each off by default and applied only where meaning-preservation is demonstrable (FR-008/009/010).

```csharp
public interface INormalizer
{
    FixClass Class { get; }
    // Returns the normalised request, or null when it cannot prove this request is safe to touch.
    bool TryNormalize(ChatRequest request, out ChatRequest normalised);
}
```

## LineEndingNormalizer (`line_ending`)

- **Transform**: CRLF and lone CR → LF in text content of the request.
- **Safe because**: line-ending style is not semantically meaningful to the model.
- **Skips**: never touches non-text parts, tool results the caller marked opaque, or base64/binary.
- **Proof**: response-transparency test — same request with mixed vs normalised endings returns an
  equivalent response (SC-004).

## TrailingWhitespaceNormalizer (`trailing_whitespace`)

- **Transform**: strip trailing spaces/tabs at end of each line in text content.
- **Safe because**: trailing whitespace carries no meaning to the model.
- **Skips**: whitespace-significant contexts (e.g. fenced code where trailing space is load-bearing are
  left alone — the normaliser proves the line is outside such a context or skips).
- **Proof**: response-transparency test.

## ToolOrderingNormalizer (`tool_ordering`)

- **Transform**: sort tool definitions by name and canonicalise each tool's JSON key order.
- **Safe because**: the wire format presents tools as an unordered set; the model is given the same set.
- **Skips**: applied only where the provider treats tools as a set; if a request's shape cannot be
  proven set-safe, it is skipped.
- **Proof**: response-transparency test asserting equivalent tool-selection behaviour on the same set.

## Rules

- **Off by default**, enabled per class via `CacheHygieneOptions` and controllable by an OmnisVigil
  policy (FR-012).
- **Skip, don't force**: `TryNormalize` returns false rather than applying a transform it cannot prove
  safe on that request; the miss is then reported measured-only (FR-009).
- **Prose and message content are never mutated** — warn-only, per the design's non-goals.
- Applied before dispatch, within the in-path budget; over budget or throwing → skipped, request
  forwarded unchanged (fail-open).
