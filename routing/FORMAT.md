# Routing-model artifact format

This defines the on-disk format for the `centroids-<ver>.bin` / `policy-<ver>.json` pair described
in `routing/README.md`. It is produced today by the seed generator
(`src/OmnisRouter.RoutingModel.Build/seed/SeedModelGenerator.cs`, `dotnet run -- seed`) as
`centroids-seed.bin` / `policy-seed.json`, and will later be produced by the reproducible offline
build (T065/T066) as `centroids-<ver>.bin` / `policy-<ver>.json`. Both producers write the same
format so the loader (T025) does not need to special-case the seed.

## `centroids-<ver>.bin`

Fixed-size binary header followed by a flat array of `float32` centroid components. Everything is
**little-endian** (matches .NET's `BinaryWriter`/`BinaryReader` defaults on all supported platforms).

| Offset (bytes) | Size | Type    | Field           | Notes                                             |
|----------------|------|---------|-----------------|----------------------------------------------------|
| 0               | 4    | ASCII   | `magic`         | Literal `"OMRC"`, **not** null-terminated           |
| 4               | 4    | int32   | `format_version`| Currently `1`                                       |
| 8               | 4    | int32   | `k`              | Cluster count                                       |
| 12              | 4    | int32   | `dim`            | Embedding dimensionality (384 for bge-small-en-v1.5)|
| 16              | `k*dim*4` | float32[] | `centroids` | Row-major: centroid 0's `dim` floats, then centroid 1's, … |

Total file size = `16 + k * dim * 4` bytes. For the seed (`k=4`, `dim=384`) that is
`16 + 4*384*4 = 6,160` bytes.

Each centroid **should** be unit-length (the seed generator normalizes every row) so the loader and
`ClusterScorerPolicy` (T026) can do a plain dot product for cosine similarity instead of re-normalizing
at request time.

### Reading it (reference pseudocode)

```csharp
using var reader = new BinaryReader(File.OpenRead(path));
var magic = new string(reader.ReadChars(4));         // "OMRC"
var formatVersion = reader.ReadInt32();
var k = reader.ReadInt32();
var dim = reader.ReadInt32();

var centroids = new float[k][];
for (var c = 0; c < k; c++)
{
    centroids[c] = new float[dim];
    for (var i = 0; i < dim; i++)
    {
        centroids[c][i] = reader.ReadSingle();
    }
}
```

## `policy-<ver>.json`

```jsonc
{
  "policy_version": "seed-2026-08-19",   // stamped on every ModelDecision/routing receipt
  "k": 4,                                  // must equal the paired .bin's k
  "dim": 384,                              // must equal the paired .bin's dim
  "clusters": [
    {
      "cluster_id": 0,                     // 0-based, indexes into the .bin's centroid rows
      "candidates": [
        {
          "provider": "openrouter",
          "model_id": "meta-llama/llama-3.3-70b-instruct",
          "predicted_quality": 0.55,        // 0-1 estimate; benchmark-derived once T065/T066 lands
          "rank_by_cost": 1                 // 1 = cheapest; candidates are listed cheap→strong
        }
        // ... more candidates, ranked cheap → strong
      ]
    }
    // ... one entry per cluster_id in [0, k)
  ]
}
```

**Validation the loader (T025) performs** (per `data-model.md`'s `RoutingModel` entity):
- `dim` matches the pinned embedder's `Dimension` (384).
- `k` equals the paired `.bin`'s `k`, and every `cluster_id` in `[0, k)` has exactly one entry.
- Every `(provider, model_id)` referenced by a candidate exists in `config/models.yaml`'s candidate pool.

## Matched pairs, never mixed

A `.bin` and its `.json` share one `<ver>` (here, `seed`) and are always loaded together. Never
regenerate one without the other — `Generate()` in `SeedModelGenerator` always writes both in one
call for exactly this reason.
