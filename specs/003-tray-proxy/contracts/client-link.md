# Contract: client link (connect / revert)

The deterministic transformation each supported client undergoes when connected to the local router,
and the exact revert. This is a guaranteed output surface (Principle IV) and is golden-tested,
including the revert and the malformed-input refusal (Principle III). `ROOT` = `http://127.0.0.1:<port>`,
`TOKEN` = the router management token.

## Claude Code

- **Target**: `~/.claude/settings.json` (JSON).
- **Connect**: merge into the top-level `env` object, without disturbing other keys:
  - `ANTHROPIC_BASE_URL` = `ROOT` (router root, no `/v1`).
  - `ANTHROPIC_AUTH_TOKEN` = `TOKEN`.
- **Capture for revert**: for each of those two keys, record whether it was present and its prior
  value. Write a timestamped backup `settings.json.bak-<ts>` before editing.
- **Revert**: for each key, restore the prior value if it was present, or remove it if it was absent,
  leaving every other key untouched.
- **Refusal**: if the existing file is not valid JSON, refuse and report; do not write.

## Codex

- **Target**: `~/.codex/config.toml` (TOML) plus a user-scoped environment variable.
- **Connect**:
  - Insert (or replace, idempotently) a delimited managed block bounded by
    `# >>> omnisrouter-cli managed block >>>` and `# <<< omnisrouter-cli managed block <<<`, defining
    `[model_providers.omnisrouter]` with `base_url = "ROOT/v1"`, `env_key = "OMNISROUTER_API_KEY"`,
    `wire_api = "chat"`.
  - Set the user environment variable `OMNISROUTER_API_KEY` = `TOKEN` (the block references the token
    by env var, not inline).
- **Capture for revert**: whether a managed block already existed (and its content) and whether
  `OMNISROUTER_API_KEY` was already set (and its prior value). Timestamped backup before editing.
- **Revert**: remove the managed block (restoring any prior block content), leaving the rest of the
  file intact, and unset or restore `OMNISROUTER_API_KEY`.
- **Refusal**: if the file cannot be parsed well enough to locate/replace the block safely, refuse and
  report.

## Cursor

- **Target**: none. Cursor has no safe scriptable config.
- **Connect**: show the values to paste into Cursor Settings → Models, with copy actions:
  - `OPENAI_BASE_URL` = `ROOT/v1`.
  - `OPENAI_API_KEY` = `TOKEN`.
  - Mark connected only when the user confirms they applied it (best-effort tracking).
- **Revert**: no file change; remind the user to clear those values in Cursor.

## Cross-cutting rules

- Every connect that edits a file writes a timestamped backup first and records enough prior state for
  an exact revert.
- Connect is idempotent: connecting an already-connected client re-applies the same result without
  stacking duplicates.
- The transforms are pure and deterministic given `(ROOT, TOKEN, prior file content)`, so they can be
  golden-tested.
- Reverting a client removes it from `connectedClients`, returning it to the collector's scope.
