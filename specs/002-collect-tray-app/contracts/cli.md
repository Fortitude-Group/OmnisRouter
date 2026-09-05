# Contract: `omnisrouter collect` CLI (unchanged)

This contract is frozen by FR-020: the extraction into `OmnisRouter.Collect` must not change the CLI's flags, defaults, or output. Recorded here so the tests can assert it.

## Invocation

```
omnisrouter collect [options]
```

`Program.cs` branches on `args[0] == "collect"` and delegates to `ConsoleCollectRunner`, which drives `CollectEngine`. Every other invocation still starts the routing proxy web host.

## Flags (all preserved)

| Flag | Default | Meaning |
|---|---|---|
| `--url <u>` | OmnisVigil section `Endpoint` | Base URL |
| `--key <k>` | OmnisVigil section `ProjectKey` | Project key |
| `--root <dir>` | `~/.claude/projects` | Transcript root |
| `--since <date>` | last 90 days | Only entries on/after this date |
| `--all` | off | Backfill entire history (`Since = null`) |
| `--watch` | off | Tail for new usage after backfill |
| `--interval <s>` | 15 | Watch poll interval (min 2) |
| `--batch <n>` | 1000 | Records per ingest POST (min 1) |
| `--dry-run` | off | Read/summarise only, post nothing, no key required |

## Output (byte-for-byte preserved)

- Header block: `OmnisRouter collect (subscription observe mode)` + `transcripts` / `target` / `window` lines.
- Backfill progress line: `\r  scanned … unique … posted … dup …`.
- Summary: `unique calls`, then `new receipts … already present …` (or the dry-run `would post …`).
- Watch lines: `[HH:mm:ss] +N new receipt(s)   (session total M)` and `[HH:mm:ss] tick failed, will retry: …`.
- `watching for new usage every Ns (Ctrl+C to stop)...` and `stopped watching.`

## Test

A CLI golden test runs a small transcript fixture through `collect --dry-run` and asserts the summary output matches the current binary's output. Behavioural parity (idempotency, pricing) is covered at the engine level.
