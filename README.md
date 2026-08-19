# OmnisRouter

OmnisRouter is a self-hosted, open-core, **transparent** LLM routing proxy. It accepts requests in
the Anthropic Messages, OpenAI Chat Completions, and Gemini formats, routes each request to the
cheapest capable model in your configured candidate pool, translates to the chosen upstream's wire
format, and returns the response in the client's original format — with a **routing receipt**
attached that explains exactly which model was picked, why, and what it cost.

The routing model (embedding-based cluster scorer + policy table) is versioned and reproducible
from public data, and its `policy_version` is stamped into every routing decision.

## License

Apache License 2.0 — see [LICENSE](./LICENSE). Copyright 2026 Fortitude Omnis Group.

## Getting started

See [`specs/001-omnisrouter/quickstart.md`](./specs/001-omnisrouter/quickstart.md) for the
validation/quickstart guide.
