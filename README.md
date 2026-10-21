# ListenBrainz Weekly Jams for Jellyfin

A Jellyfin plugin that keeps a rolling Jellyfin playlist in sync with the
**Weekly Jams** playlist [ListenBrainz](https://listenbrainz.org) generates
from your listening history every week.

> **Status: v0.2.0 — working, integration-tested on Jellyfin 12.2.**
> 48/50 tracks matched on the reference library (97% MBID tagging coverage),
> full sync in ~10 s on ~19k items, unchanged weeks short-circuit to a single
> API call (~0.4 s). Requirements and verified findings:
> [`docs/requirements.md`](docs/requirements.md), design:
> [`docs/design.md`](docs/design.md).

## How it works

- A scheduled task (default: **daily at 04:00**, configurable in Dashboard →
  Scheduled Tasks) fetches the latest *Weekly Jams* playlist from the public
  ListenBrainz API for the configured username — no ListenBrainz token needed.
- **Short-circuit:** ListenBrainz issues a new playlist ID each week, and the
  plugin remembers the last-synced one. Unchanged weeks cost one API call and
  never touch the playlist. The **Sync now** button on the config page forces
  a full re-sync (e.g. after a library rescan).
- Tracks are matched to your library by **MusicBrainz recording ID** first
  (files tagged with [Picard](https://picard.musicbrainz.org) are fully
  supported), with a normalized artist/title fallback for the rest.
- One rolling playlist is created and refreshed in place — order preserved,
  renames survive, tracks missing from your library are reported in the
  server log.
- Safety: a week that matches zero tracks never wipes the playlist, and a
  failed ListenBrainz fetch never touches it.

## Installation

Requires Jellyfin 12.x.

Until the plugin is published to the catalog, install manually:

```shell
dotnet publish Jellyfin.Plugin.ListenBrainzWeeklyJams/Jellyfin.Plugin.ListenBrainzWeeklyJams.csproj -c Release
```

Copy `Jellyfin.Plugin.ListenBrainzWeeklyJams/bin/Release/net10.0/publish/Jellyfin.Plugin.ListenBrainzWeeklyJams.dll`
into `<jellyfin-data>/plugins/Jellyfin.Plugin.ListenBrainzWeeklyJams/` and
restart the server. Then configure your ListenBrainz username in
Dashboard → Plugins → ListenBrainz Weekly Jams.

## Development notes

- `dotnet test Jellyfin.Plugin.ListenBrainzWeeklyJams.slnx` — 57 unit tests.
- `scripts/dry_run.py` — read-only end-to-end validation of the sync strategy
  against a real ListenBrainz account and Jellyfin server (match rate,
  tagging coverage, missing-track report) without installing anything.
- `benchmarks/oq5-bench/` — the measurement that selected the matcher's
  paged-bulk query strategy over per-track queries.
- The repo's history doubles as the build log: requirements engineering with
  verified API facts (docs/requirements.md appendices), a benchmarked design
  decision, and an integration test that caught two real bugs before release.

## License

GPL-3.0 — see [LICENSE](LICENSE). Note that Jellyfin plugins compiled against
the Jellyfin NuGet packages are effectively GPL-3.0 regardless of source
license choice.
