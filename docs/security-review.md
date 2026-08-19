# Security review (v1)

A review of OmnisRouter's security-relevant surfaces. Verified-by-test items are noted; open findings
carry a severity and recommended mitigation.

## Verified protections (with automated tests)

| Area | Protection | Test |
|---|---|---|
| BYOK at rest | AES-256-GCM (`AesGcm`), fresh 12-byte nonce per write; master key in an OS-permission-restricted file, never in the DB | `ByokEncryptionAtRestTests` (raw SQLite blob ≠ plaintext; distinct ciphertext per encrypt) |
| No key/prompt leakage | Keys + prompts absent from logs, receipts, and the decision-log export; `SecretRedactor` scrubs log output as defense-in-depth | `NoLeakageTests`, `RedactionTests` |
| Prompt egress | Prompt content leaves only to the chosen upstream provider host | `EgressRestrictionTests` |
| No unauthorized fallback | Never routes to / calls a provider without a configured key (503 rather than a wrong key) | `MissingKeyResolverTests`, `RouteOpenAiEndToEndTests` |
| Decision log | Content-free: a non-reversible SHA-256 `request_hash`, never prompt/response text | `Us2TransparencyTests` |
| Self-host isolation | Boots with SQLite and no external dependency for core routing | `SelfHostSmokeTests` |

## Manual checklist

- **Auth**: router tokens stored as SHA-256 hashes (never plaintext); health/readiness/`/`/`/ui` are
  the only unauthenticated routes; `/ui` is static HTML that authenticates its own data fetch.
  → *Tokens must be high-entropy random* (unsalted hashing is safe only for random tokens); the
  bootstrap token should be a long random value.
- **Injection**: all DB access is EF Core parameterized; no string-concatenated SQL. Key ids from
  route params are used only as parameterized lookups.
- **Path handling**: `config/` and `routing/` paths are resolved by `RepoLocator`, not user input.
- **Error handling**: the error middleware maps to each format's error shape and never leaks stack
  traces or internals (500 → generic message).
- **Transport**: streaming uses `ResponseHeadersRead` with no retry (no prompt resend / double-charge);
  upstream calls carry only the operator's BYOK credential.

## Open findings

### F1 — SSRF in the image materializer — ✅ RESOLVED
`ImageMaterializer` fetches a **client-supplied remote image URL** when routing a request with a
remote `image_url` to a provider that can't dereference it (Anthropic/Gemini) — an SSRF surface.
**Mitigation shipped** (`SafeImageFetch`): the fetch HttpClient uses a `SocketsHttpHandler.ConnectCallback`
that resolves the host and connects **only to a public IP**, validated at connect time — which also
defeats DNS-rebinding (the IP checked is the IP connected). Non-public space (loopback, RFC-1918,
`169.254/16` incl. the cloud-metadata endpoint, CGNAT, IPv6 link-local/ULA, IPv4-mapped equivalents)
is rejected; the scheme is restricted to http(s); auto-redirect is disabled; the download is capped at
10 MB with a 10 s timeout; failures surface as a non-leaking **400**, never a route to an unsafe target.
Covered by `SsrfGuardTests` (19 address cases).

### F2 — Dependency scanning not yet in CI (LOW)
Add `dotnet list package --vulnerable`/`--outdated` (and `npm audit` for `installer/`) to the release
gate. NuGet `NU1901+` advisories currently surface only as warnings.

### F3 — Router-token entropy is operator-controlled (LOW)
Bootstrap/issued tokens rely on the operator supplying sufficient entropy. A future `/v1/tokens`
issuance endpoint should generate 256-bit random tokens server-side.

## Follow-ups (tracked)
Pin the ONNX embedder asset; add the SSRF guard (F1); wire dependency scanning into `release-gate.ps1`
(F2); server-side token issuance (F3).
