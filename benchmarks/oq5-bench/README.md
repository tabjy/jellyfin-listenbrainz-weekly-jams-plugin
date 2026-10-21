# OQ-5 benchmark: per-track vs (paged) bulk provider-ID resolution

Settles [OQ-5](../../docs/requirements.md) with measurements instead of vibes.

Mirrors Jellyfin 10.11's exact EF query shapes (`BaseItemRepository.cs` ~L2448–2462)
and schema (`JellyfinDbModelSnapshot.cs`: `BaseItemProvider` with PK `(ItemId,
ProviderId)` and index `(ProviderId, ProviderValue, ItemId)`), at the dry-run
library's scale: 19,061 audio items, 89,111 provider rows, 97.4% carrying
`MusicBrainzRecording`, a 50-track playlist.

## Results (2026-10-21, net9.0 / EF Core 9.0.11 / SQLite, warmed caches)

| Strategy | Time | Notes |
|---|---|---|
| Per-track, `HasAnyProviderId` concat shape (50 queries) | **1,427 ms** | the shape the plugin can actually emit |
| Per-track, ideal tuple shape | 932 ms | *not reachable* via the public query API — curiosity only |
| Bulk, single query, count only | 56 ms | raw scan cost (index-friendly `includeAny` predicate) |
| **Paged bulk + streaming MBID-set filter** | **658 ms** | full materialization with provider rows; 50 candidates kept, memory bounded by page size |

## Conclusion

The `ProviderId + ":" + ProviderValue` concatenation in Jellyfin's
`HasAnyProviderId` translation defeats the `(ProviderId, ProviderValue, ItemId)`
index, so each per-track query scans the provider table — and per-track cost
scales with playlist length on top of library size. The matcher therefore uses
**paged bulk with a streaming MBID-set filter**: page through all audio items
that have `MusicBrainzRecording` set, keep only candidates matching the
playlist's recording-MBID set (all candidates, feeding OQ-6), discard each page.
Name fallback issues per-miss queries only (2–3 per sync in practice).

Self-contained (not part of the plugin solution):

```shell
dotnet run -c Release
```
