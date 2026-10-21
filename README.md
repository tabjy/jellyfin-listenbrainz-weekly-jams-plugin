# ListenBrainz Weekly Jams for Jellyfin

A Jellyfin plugin that keeps a rolling Jellyfin playlist in sync with the
**Weekly Jams** playlist [ListenBrainz](https://listenbrainz.org) generates
from your listening history every week.

> **Status: in development.** Requirements are locked and validated against
> a real account and server (96% match rate — see `docs/requirements.md`,
> Appendix A.3). The sync engine is not yet implemented.

## How it will work

- A scheduled task (default: daily) fetches the latest *Weekly Jams* playlist
  from the public ListenBrainz API for the configured username — no
  ListenBrainz token needed.
- If the playlist hasn't changed since the last sync (ListenBrainz issues a
  new playlist ID each week), the sync short-circuits after a single API call.
- Tracks are matched to your library by MusicBrainz recording ID first
  (files tagged with [Picard](https://picard.musicbrainz.org) are fully
  supported), with a normalized artist/title fallback.
- One rolling Jellyfin playlist is created and refreshed in place — order
  preserved, unmatched tracks skipped and reported in the server log.

Full requirements, verified API facts, and design decisions:
[`docs/requirements.md`](docs/requirements.md).

## Build

Requires the .NET 9 SDK.

```shell
dotnet publish Jellyfin.Plugin.ListenBrainzWeeklyJams.slnx -c Release
```

The plugin DLL is output under
`Jellyfin.Plugin.ListenBrainzWeeklyJams/bin/Release/net9.0/publish/`. Copy it
into `<jellyfin-data>/plugins/Jellyfin.Plugin.ListenBrainzWeeklyJams/` and
restart the server.

## Development notes

- `scripts/dry_run.py` — read-only end-to-end validation of the sync strategy
  against a real ListenBrainz account and Jellyfin server (useful for
  checking match rate and tagging coverage without installing anything):

  ```shell
  python3 scripts/dry_run.py --lb-user <name> --jf-url <url> --jf-key <apikey>
  ```

- `scripts/test_dry_run.py` — unit tests for the matching logic
  (`python3 -m unittest discover -s scripts -p 'test_*.py'`).

## License

GPL-3.0 — see [LICENSE](LICENSE). Note that Jellyfin plugins compiled against
the Jellyfin NuGet packages are effectively GPL-3.0 regardless of source
license choice.
