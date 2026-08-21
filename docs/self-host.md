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
| BYOK master key | `%APPDATA%\OmnisRouter\master.key` (Windows) / `$XDG_CONFIG_HOME/OmnisRouter/master.key` else `~/.config/OmnisRouter/master.key` (Linux/macOS) / `/data/OmnisRouter/master.key` (Docker) | `Byok:MasterKeyPath` (else OS default) |

`LocalFileMasterKeyProvider` (`src/OmnisRouter.Upstream/Security/LocalFileMasterKeyProvider.cs`)
generates a random 256-bit key on first run if the file doesn't exist, and restricts its permissions
to the current user (ACL on Windows, `0600` on Linux/macOS). **`ProviderKey.ApiKey` is encrypted
with AES-256-GCM under this key** (`AesGcmSecretCipher`) — lose the master key and every stored
BYOK provider key becomes permanently undecryptable, even though the database file itself is intact.

**Back up the database file and the master key together, as a unit.** In Docker both live under the
same `/data` volume, so backing up that volume covers both. On bare metal, back up the SQLite file
alongside `%APPDATA%\OmnisRouter\` / `~/.config/OmnisRouter/`.

> Note: the master-key path is configurable via `Byok:MasterKeyPath` (env `Byok__MasterKeyPath`) —
> `Program.cs` binds it through to `MasterKeyOptions.KeyFilePath`. Leave it unset to get the OS
> default above (the Docker image redirects that default into `/data` by setting
> `XDG_CONFIG_HOME=/data`). On bare metal you can point it at an explicit file with
> `Byok__MasterKeyPath`, or set `XDG_CONFIG_HOME` (Linux/macOS) / run the process under an account
> whose `%APPDATA%` points where you want (Windows).

## Applying database migrations

`Program.cs` calls `Database.Migrate()` on startup, so the schema is created and upgraded
automatically the first time the app runs, and again after any upgrade that ships new migrations —
you don't have to run a migration step by hand for either run path.

- **Docker**: belt-and-suspenders. `deploy/docker-entrypoint.sh` also runs the migration bundle
  matching `Database__Provider` against `ConnectionStrings__Default` before launching the app, so
  the schema is current even if you swap in a build without the startup migrate. Both paths are
  idempotent — a run against an up-to-date schema is a no-op.
- **Bare-metal binary**: nothing required — the startup migrate handles it. If you'd rather
  provision the schema ahead of first start (or just inspect it), you can still apply migrations
  explicitly:

  ```powershell
  dotnet tool install --global dotnet-ef   # once, if you don't already have it
  dotnet ef database update `
    --project src/OmnisRouter.Store.Migrations.Sqlite `
    --startup-project src/OmnisRouter.Api `
    --connection "Data Source=omnisrouter.db"
  ```

  Swap the `--project` for `OmnisRouter.Store.Migrations.Npgsql` and the connection string when
  running against Postgres (see below).

## Adding a router token + BYOK provider keys

**Client-facing router tokens** authenticate every request except `/health`, `/readyz`, `/`, and
`/ui` (`RouterTokenAuthMiddleware`). A token is only ever stored hashed (SHA-256 over the raw token,
hex — `RouterTokenHasher`); the raw value never touches the database.

**Provider (BYOK) keys** are your own Anthropic/OpenAI/Gemini/OpenRouter API keys, stored encrypted
under the master key above (`ProviderKey.ApiKey`, via an EF Core `ValueConverter` that calls
`ISecretCipher.Encrypt` — ciphertext is written, never plaintext, and it's decrypted transparently
on read).

### Seeding the first router token (the auth chicken-and-egg)

On startup, if the token store is empty **and** `Omnis:BootstrapToken` is set, `Program.cs` seeds a
single token row for tenant `default` from that value. Set it as an env var and start the app:

```powershell
$env:Omnis__BootstrapToken = "<a-long-random-string-you-generate>"
```

In Docker, add `Omnis__BootstrapToken` to the `omnisrouter` service `environment:` block (or a
compose override file). The seed only fires while the token table is empty, so it's safe to leave
set across restarts — once a token exists the value is ignored, not re-applied. From then on, call
the router with `Authorization: Bearer <that-value>`.

### Managing provider keys — `/v1/keys`

`src/OmnisRouter.Api/Endpoints/Keys.cs` is wired into `Program.cs` (`app.MapKeys()`), so the
key-management endpoints are live: `POST /v1/keys`, `GET /v1/keys`, `DELETE /v1/keys/{id}`. All of
them require `Authorization: Bearer <router-token>`, and the plaintext key value never crosses the
wire in either direction — only `id`, `provider`, `label`, and `created_at` do.

Add a key:

```bash
curl -X POST http://localhost:8080/v1/keys \
  -H "Authorization: Bearer <router-token>" -H "Content-Type: application/json" \
  -d '{"provider":"anthropic","label":"primary","api_key":"sk-ant-..."}'
# 201 -> {"id":"...","provider":"anthropic","label":"primary","created_at":"..."}
```

`provider` must be one of `openai`, `anthropic`, `gemini`, `openrouter`; `label` and `api_key` are
both required. List keys with `GET /v1/keys` (same id/provider/label/created_at shape, newest first)
and remove one with `DELETE /v1/keys/{id}` (returns `204`).

> A routing decision needs at least one provider key: `POST /v1/route` returns
> `503 no_routable_models` until a candidate model is both reachable (an upstream client exists) and
> usable (a BYOK key is configured for its provider).

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
