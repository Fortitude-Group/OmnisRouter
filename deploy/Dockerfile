# OmnisRouter self-host image (T061).
#
# Two-stage build: the SDK stage restores/publishes the API (framework-dependent — the runtime
# stage already carries the ASP.NET runtime) and produces two self-contained EF Core "migrations
# bundle" executables (one per supported Database:Provider). Program.cs applies migrations at
# startup (Database.Migrate()); docker-entrypoint.sh also runs the bundle matching Database__Provider
# against the mounted volume before the app starts, as belt-and-suspenders. Both are idempotent —
# a run against an up-to-date schema is a no-op.
#
# The runtime stage also fetches the pinned ONNX embedder asset (bge-small-en-v1.5) so the image
# ships the real embedder. Without it the app falls back to the non-production HashingEmbedder,
# whose vectors don't share a space with the ONNX-built routing centroids — so every request would
# escalate to the strong default instead of routing.
#
# Build from the repo root:
#   docker build -f deploy/Dockerfile -t omnisrouter:latest .

# ---------------------------------------------------------------------------
# Stage 1: build + publish + EF migration bundles
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching): copy solution/props + every csproj, preserving paths.
COPY OmnisRouter.slnx Directory.Build.props ./
COPY src/OmnisRouter.Api/OmnisRouter.Api.csproj src/OmnisRouter.Api/
COPY src/OmnisRouter.Adapters/OmnisRouter.Adapters.csproj src/OmnisRouter.Adapters/
COPY src/OmnisRouter.Core/OmnisRouter.Core.csproj src/OmnisRouter.Core/
COPY src/OmnisRouter.Routing/OmnisRouter.Routing.csproj src/OmnisRouter.Routing/
COPY src/OmnisRouter.RoutingModel.Build/OmnisRouter.RoutingModel.Build.csproj src/OmnisRouter.RoutingModel.Build/
COPY src/OmnisRouter.Store/OmnisRouter.Store.csproj src/OmnisRouter.Store/
COPY src/OmnisRouter.Store.Migrations.Sqlite/OmnisRouter.Store.Migrations.Sqlite.csproj src/OmnisRouter.Store.Migrations.Sqlite/
COPY src/OmnisRouter.Store.Migrations.Npgsql/OmnisRouter.Store.Migrations.Npgsql.csproj src/OmnisRouter.Store.Migrations.Npgsql/
COPY src/OmnisRouter.Telemetry/OmnisRouter.Telemetry.csproj src/OmnisRouter.Telemetry/
COPY src/OmnisRouter.Upstream/OmnisRouter.Upstream.csproj src/OmnisRouter.Upstream/
RUN dotnet restore src/OmnisRouter.Api/OmnisRouter.Api.csproj

# Now bring in the rest of the source and publish.
COPY src/ src/

RUN dotnet publish src/OmnisRouter.Api/OmnisRouter.Api.csproj \
      -c Release \
      -o /app/publish \
      --no-restore

# EF Core migrations bundles: self-contained native executables that apply the current
# OmnisRouterDbContext migrations to a target connection string without needing the SDK (or even
# the `dotnet-ef` tool) in the runtime image. One per provider, since Database:Provider is a
# runtime choice (config), not a build-time one.
#
# TARGETARCH is populated automatically by BuildKit for the platform being built (amd64/arm64) —
# mapped to the matching linux RID so this also works unmodified on arm64 hosts (e.g. Apple
# Silicon Docker Desktop), not just amd64.
ARG TARGETARCH
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"

RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac \
 && dotnet ef migrations bundle \
      --project src/OmnisRouter.Store.Migrations.Sqlite/OmnisRouter.Store.Migrations.Sqlite.csproj \
      --self-contained -r "$RID" \
      --output /app/publish/efbundle-sqlite \
      --force \
 && dotnet ef migrations bundle \
      --project src/OmnisRouter.Store.Migrations.Npgsql/OmnisRouter.Store.Migrations.Npgsql.csproj \
      --self-contained -r "$RID" \
      --output /app/publish/efbundle-npgsql \
      --force

# ---------------------------------------------------------------------------
# Stage 2: runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl for docker-compose.yml's healthcheck — not present in the base aspnet image.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Non-root user. /data is where the SQLite DB + BYOK master key live (see docs/self-host.md); it's
# created here (owned by the app user) so a fresh named volume inherits the right ownership on
# first mount.
RUN groupadd --system omnisrouter \
 && useradd --system --gid omnisrouter --home-dir /app --no-create-home omnisrouter \
 && mkdir -p /data \
 && chown -R omnisrouter:omnisrouter /app /data

COPY --from=build --chown=omnisrouter:omnisrouter /app/publish ./

# Data directories the app resolves relative to its own working directory via RepoLocator
# (src/OmnisRouter.Routing/RepoLocator.cs) — must sit next to the binary, not just in the repo,
# since there's no OmnisRouter.slnx marker in this image to walk up to.
COPY --chown=omnisrouter:omnisrouter config/ ./config/
COPY --chown=omnisrouter:omnisrouter routing/ ./routing/

# In-process embedder asset (bge-small-en-v1.5, int8 ONNX). Pinned URL + SHA-256 kept in lockstep
# with scripts/fetch-embedder.ps1 — the routing centroids in routing/ are built from this exact
# model, so it must be present for query embeddings to match them. The app auto-detects it at
# models/bge-small-en-v1.5/ (RepoLocator, relative to this WORKDIR) and uses OnnxEmbedder; if it's
# missing it silently falls back to HashingEmbedder and every request escalates.
ARG EMBEDDER_MODEL_URL=https://huggingface.co/Xenova/bge-small-en-v1.5/resolve/main/onnx/model_quantized.onnx
ARG EMBEDDER_MODEL_SHA=6c9c6101a956d62dfb5e7190c538226c0c5bb9cb27b651234b6df063ee7dbfe4
ARG EMBEDDER_VOCAB_URL=https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/main/vocab.txt
RUN mkdir -p /app/models/bge-small-en-v1.5 \
 && curl -fSL "$EMBEDDER_MODEL_URL" -o /app/models/bge-small-en-v1.5/model.onnx \
 && echo "${EMBEDDER_MODEL_SHA}  /app/models/bge-small-en-v1.5/model.onnx" | sha256sum -c - \
 && curl -fSL "$EMBEDDER_VOCAB_URL" -o /app/models/bge-small-en-v1.5/vocab.txt \
 && chown -R omnisrouter:omnisrouter /app/models

COPY --chown=omnisrouter:omnisrouter deploy/docker-entrypoint.sh ./docker-entrypoint.sh
RUN chmod +x ./docker-entrypoint.sh ./efbundle-sqlite ./efbundle-npgsql

# System.Environment.GetFolderPath(ApplicationData) on Linux resolves under $XDG_CONFIG_HOME (else
# $HOME/.config). LocalFileMasterKeyProvider's default KeyFilePath
# (src/OmnisRouter.Upstream/Security/MasterKeyOptions.cs) uses that folder — pointing it at /data
# keeps the master key inside the same mounted volume as the database instead of the container's
# writable layer, so both survive a container recreate.
ENV XDG_CONFIG_HOME=/data
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
VOLUME ["/data"]

USER omnisrouter

ENTRYPOINT ["./docker-entrypoint.sh"]
CMD ["dotnet", "OmnisRouter.Api.dll"]
