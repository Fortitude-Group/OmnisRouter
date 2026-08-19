#!/bin/sh
# Applies pending EF Core migrations for the configured Database:Provider (via the
# self-contained efbundle-* executables baked into the image by deploy/Dockerfile), then execs the
# app. Idempotent — a bundle run against an up-to-date schema is a no-op — so this is safe to run
# on every container start, which is what lets us keep Program.cs free of a startup
# Database.Migrate() call (see the Dockerfile's stage-1 comment for why).
set -eu

# Config keys use ASP.NET Core's double-underscore env-var convention:
#   Database:Provider        -> Database__Provider
#   ConnectionStrings:Default -> ConnectionStrings__Default
PROVIDER="${Database__Provider:-Sqlite}"
CONNECTION="${ConnectionStrings__Default:-Data Source=/data/omnisrouter.db}"

case "$(printf '%s' "$PROVIDER" | tr '[:upper:]' '[:lower:]')" in
  npgsql | postgres | postgresql)
    BUNDLE=./efbundle-npgsql
    ;;
  *)
    BUNDLE=./efbundle-sqlite
    ;;
esac

echo "docker-entrypoint: applying migrations via ${BUNDLE} (Database:Provider=${PROVIDER})"
"${BUNDLE}" --connection "${CONNECTION}"

echo "docker-entrypoint: migrations applied, starting app"
exec "$@"
