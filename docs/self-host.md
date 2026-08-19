# Self-hosting OmnisRouter

Operator guide for running OmnisRouter yourself (FR-015: single self-contained process, no
external service required for core routing). Two supported run paths — pick one:

| | Single-file binary | Docker Compose |
|---|---|---|
| Prerequisites | Nothing (self-contained) | Docker + Compose |
| Best for | Bare-metal / VM, systemd unit | Everything else |
| Data location | Wherever you point `ConnectionStrings:Default` / the master key | Named volume `/data` |

If you'd rather run from source: .NET 10 SDK, then `dotnet run --project src/OmnisRouter.Api`
(see the repo [README](../README.md) and [quickstart](../specs/001-omnisrouter/quickstart.md)).

## Run path 1 — single-file binary

```powershell
dotnet publish src/OmnisRouter.Api -p:PublishProfile=selfhost -r linux-x64   # or win-x64 / osx-arm64
```

This uses [`src/OmnisRouter.Api/Properties/PublishProfiles/selfhost.pubxml`](../src/OmnisRouter.Api/Properties/PublishProfiles/selfhost.pubxml)
— self-contained, single-file, no separate .NET runtime install needed on the target machine. Output
lands in `bin/Release/net10.0/<rid>/publish/`.

The publish output does **not** include `config/` or `routing/` — the app resolves those relative to
its own working directory (`RepoLocator`, see below), not as embedded content. Copy them alongside
the executable:

```powershell
Copy-Item -Recurse config,routing bin/Release/net10.0/linux-x64/publish/
```

Then apply migrations (see below) and run the executable from that directory.

### Why `config/` and `routing/` must sit next to the binary

`RepoLocator.Resolve` (`src/OmnisRouter.Routing/RepoLocator.cs`) first checks the path relative to
the process's current working directory; only if that misses does it walk up from the app's base
directory looking for `OmnisRouter.slnx` as a marker. A published binary running outside the repo
has no `.slnx` to find, so the CWD-relative check is what actually resolves `config/models.yaml`,
`config/pricing/`, and `routing/` in production — **run the binary from the publish directory** with
those two folders copied in.

## Run path 2 — Docker Compose

```powershell
docker compose -f deploy/docker-compose.yml up -d --build
```

[`deploy/Dockerfile`](../deploy/Dockerfile) builds the app plus a set of self-contained EF Core
migration-bundle executables, and copies `config/` + `routing/` into the image next to the binary
(same reasoning as above). [`deploy/docker-compose.yml`](../deploy/docker-compose.yml) runs one
`omnisrouter` service, embedded SQLite by default, with a named volume (`omnisrouter-data`) mounted
at `/data` for the database and BYOK master key, and a healthcheck against `/health`.

## Data: where it lives and how to back it up

| What | Default location | Config key |
|---|---|---|
| SQLite database | `omnisrouter.db` in the working directory (binary) / `/data/omnisrouter.db` (Docker) | `ConnectionStrings:Default` |
| BYOK master key | `%APPDATA%\OmnisRouter\master.key` (Windows) / `$XDG_CONFIG_HOME/OmnisRouter/master.key` else `~/.config/OmnisRouter/master.key` (Linux/macOS) / `/data/OmnisRouter/master.key` (Docker) | not currently configurable — see note below |

`LocalFileMasterKeyProvider` (`src/OmnisRouter.Upstream/Security/LocalFileMasterKeyProvider.cs`)
generates a random 256-bit key on first run if the file doesn't exist, and restricts its permissions
to the current user (ACL on Windows, `0600` on Linux/macOS). **`ProviderKey.ApiKey` is encrypted
with AES-256-GCM under this key** (`AesGcmSecretCipher`) — lose the master key and every stored
BYOK provider key becomes permanently undecryptable, even though the database file itself is intact.

**Back up the database file and the master key together, as a unit.** In Docker both live under the
same `/data` volume, so backing up that volume covers both. On bare metal, back up the SQLite file
alongside `%APPDATA%\OmnisRouter\` / `~/.config/OmnisRouter/`.

> Note: `MasterKeyOptions.KeyFilePath` has no configuration binding today — `Program.cs` calls
> `AddOmnisByok()` with no options callback, so the path is always the OS default above (the Docker
> image redirects it into `/data` by setting `XDG_CONFIG_HOME=/data`, not by configuring the option).
> If you need a custom path on bare metal, set `XDG_CONFIG_HOME` (Linux/macOS) or run the process
> under an account whose `%APPDATA%` points where you want (Windows) — there's no dedicated app
> setting for this yet.

## Applying database migrations

`Program.cs` does **not** call `Database.Migrate()` at startup (only the integration-test host does,
in `tests/OmnisRouter.Api.Tests/OmnisApiFactory.cs`) — so the schema has to be created/upgraded
explicitly before the app can serve requests.

- **Docker**: handled for you. `deploy/docker-entrypoint.sh` runs the migration bundle matching
  `Database__Provider` against `ConnectionStrings__Default` on every container start (a no-op once
  the schema is current) before launching the app.
- **Bare-metal binary**: run this once before first start, and again after any upgrade that ships
  new migrations:

  ```powershell
  dotnet tool install --global dotnet-ef   # once, if you don't already have it
  dotnet ef database update `
    --project src/OmnisRouter.Store.Migrations.Sqlite `
    --startup-project src/OmnisRouter.Api `
    --connection "Data Source=omnisrouter.db"
  ```

  Swap the `--project` for `OmnisRouter.Store.Migrations.Npgsql` and the connection string when
  running against Postgres (see below).

## Adding a BYOK provider key + a router token

**Client-facing router tokens** authenticate every request except `/health`, `/readyz`, and `/`
(`RouterTokenAuthMiddleware`). A token is only ever stored hashed (SHA-256 over the raw token, hex —
`RouterTokenHasher`) — the raw value is shown to the operator exactly once at creation time.

**Provider (BYOK) keys** are your own Anthropic/OpenAI/Gemini/OpenRouter API keys, stored encrypted
under the master key above (`ProviderKey.ApiKey`, via an EF Core `ValueConverter` that calls
`ISecretCipher.Encrypt` — the ciphertext is written, never plaintext, and it's decrypted transparently
on read).

The intended way to manage provider keys is the dedicated key-management endpoint at **`/v1/keys`**
(spec task T059). `src/OmnisRouter.Api/Endpoints/Keys.cs` exists in the tree with `POST/GET /v1/keys`
and `DELETE /v1/keys/{id}` (add/list/delete `ProviderKey` rows — plaintext never crosses the wire in
either direction, only id/provider/label/created_at), **but as of this doc it is not yet wired into
`Program.cs`** (no `app.MapKeys()` call) and `specs/001-omnisrouter/tasks.md` still shows T059
unchecked — so the endpoint isn't reachable yet. There is also no endpoint anywhere yet for *issuing*
a router token (`RouterToken` rows) — `/v1/keys` as it stands only covers provider keys. Once T059
finishes (endpoint wired + token issuance added), update the table above with the exact request
shape.

**Until then**, there is no supported way to seed a `RouterToken` or `ProviderKey` from outside the
running process:

- `RouterToken.HashedToken` just needs `RouterTokenHasher.Hash(rawToken)` — a stateless SHA-256, so
  it *could* be precomputed and inserted with raw SQL — but
- `ProviderKey.ApiKey` cannot be hand-crafted via SQL: the stored bytes are
  `keyVersion(4) ‖ nonce(12) ‖ tag(16) ‖ ciphertext` (AES-256-GCM, `AesGcmSecretCipher`), which
  requires the live master key and a fresh random nonce per row — that only happens inside the
  app's own `ISecretCipher`.

So the practical bootstrap path pre-T059 is a short throwaway console app that reuses the app's own
DI wiring to insert both rows correctly:

```csharp
// scratch console app, referencing OmnisRouter.Store + OmnisRouter.Upstream + OmnisRouter.Core
var services = new ServiceCollection();
services.AddOmnisByok();                                   // real cipher, real master key
services.AddOmnisStore(configuration);                      // point Database:Provider / ConnectionStrings:Default at your target DB
var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();

db.RouterTokens.Add(new RouterToken {
    Id = Guid.NewGuid().ToString(), TenantId = "default", Name = "bootstrap",
    HashedToken = RouterTokenHasher.Hash(rawToken), CreatedAt = DateTimeOffset.UtcNow,
});
db.ProviderKeys.Add(new ProviderKey {
    Id = Guid.NewGuid().ToString(), TenantId = "default", Provider = Provider.Anthropic,
    Label = "primary", ApiKey = "sk-ant-...", KeyVersion = 1, CreatedAt = DateTimeOffset.UtcNow,
});
await db.SaveChangesAsync();
```

Run it once against the same database/master-key the router will use, note the `rawToken` you
generated, and use it as `Authorization: Bearer <rawToken>` from then on.

## Pointing clients at the router

Point each client at the router's base URL instead of the provider's, keeping its own client
library/SDK unchanged — the router auto-detects the format from the endpoint path:

| Client format | Endpoint |
|---|---|
| OpenAI Chat Completions | `POST {base_url}/v1/chat/completions` |
| Anthropic Messages | `POST {base_url}/v1/messages` |
| Gemini `generateContent` | `POST {base_url}/v1beta/models/{model}:generateContent` |

All three require `Authorization: Bearer <router-token>`. Other useful endpoints: `POST /v1/route`
(dry-run — full decision, no upstream call/cost), `GET /v1/models`, `GET /v1/analytics/routing-decisions`
(NDJSON decision log).

## Switching to Postgres

Set `Database:Provider=Postgres` and point `ConnectionStrings:Default` at your server; `Sqlite`
(default) needs neither:

```powershell
$env:Database__Provider = "Postgres"
$env:ConnectionStrings__Default = "Host=localhost;Database=omnisrouter;Username=omnisrouter;Password=***"
```

In `deploy/docker-compose.yml`, uncomment the `postgres` service and the matching `Database__Provider`
/ `ConnectionStrings__Default` lines (and comment out the SQLite ones) — the commented Postgres block
is left in place for exactly this. Run migrations against the `OmnisRouter.Store.Migrations.Npgsql`
project (see above) instead of the Sqlite one; the Docker entrypoint picks this automatically from
`Database__Provider`.

## OTLP telemetry

Set `Otlp:Endpoint` (or the standard `OTEL_EXPORTER_OTLP_ENDPOINT`/OTel SDK env vars, which are used
as a fallback when `Otlp:Endpoint` is unset) to export traces + metrics to a collector:

```powershell
$env:Otlp__Endpoint = "http://localhost:4317"
```

Leave it unset to keep the OpenTelemetry SDK's own default/no-op exporter behavior.

## Health and readiness probes

Both are exempt from router-token auth (`RouterTokenAuthMiddleware.ExemptPaths`):

- `GET /health` — liveness. Always `200 {"status":"ok"}` once the process is serving requests.
- `GET /readyz` — readiness. `200 {"status":"ready"}` when the configured database is reachable,
  otherwise `503 {"status":"not_ready"}`.

`deploy/docker-compose.yml`'s healthcheck uses `/health`. Point your orchestrator's readiness probe
at `/readyz` if you want it to hold traffic until the DB connection is actually up (e.g. right after
a Postgres failover).
