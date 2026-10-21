# Requirements — ListenBrainz Weekly Jams for Jellyfin

| | |
|---|---|
| **Status** | Draft v0.3 — for review |
| **Date** | 2026-10-20 |
| **Target** | Jellyfin 12.2 (`targetAbi 12.0.0.0`, verified against `v12.2` source), .NET 10 (`net10.0`) — retargeted from 10.11 on 2026-10-21 when integration pre-flight showed the deployment server runs 12.2.0 |
| **Name** | **ListenBrainz Weekly Jams**, assembly `Jellyfin.Plugin.ListenBrainzWeeklyJams` (OQ-1 resolved) |

---

## 1. Vision

A Jellyfin server plugin that keeps a single, always-fresh Jellyfin playlist mirror of the
user's **ListenBrainz Weekly Jams** — the playlist ListenBrainz generates every week from
the tracks the listener already loves. The listener opens Jellyfin anywhere (TV, phone,
car) and their weekly mix is just… there. No exporting, no m3u juggling, no manual
copying.

> Weekly Jams (ListenBrainz): *"songs that you have listened to before, arranged into a
> comfortable playlist that doesn't require active listening on your part."* — regenerated
> weekly (Monday mornings, per the user's timezone setting), personalized per listener.

## 2. Background & system context

The plugin runs inside a Jellyfin 10.11 server (fresh scaffold from the official plugin
template, `net9.0`). It talks to the public ListenBrainz API and uses Jellyfin's internal
library/playlist services.

### 2.1 ListenBrainz API facts (verified live 2026-10-20)

Endpoint calls were made against production (`api.listenbrainz.org`) to confirm payload
shapes. **Note:** the live response shapes differ slightly from the readthedocs
descriptions (no `payload` wrapper) — our models must follow reality, not the docs.

| Fact | Detail |
|---|---|
| List "created for you" playlists | `GET /1/user/{user}/playlists/createdfor` — top-level `{count, offset, playlist_count, playlists: [{playlist: {...}}]}`. **Paginated**: page size 25; observed `playlist_count: 28` → pagination is mandatory. Public, no auth. |
| Created-for-you playlist metadata | Each entry: `title`, `creator` ("listenbrainz"), **`date`** (creation timestamp), `identifier` (playlist MBID as URL), `annotation` (HTML description), `extension` → `algorithm_metadata.source_patch`, `created_for`, `expires_at`, `last_modified_at`. |
| Structured type ID | `source_patch` ∈ `weekly-jams`, `weekly-exploration`, `top-discoveries-of-{year}`, `top-missed-recordings-of-{year}`. **Type selection needs no title parsing.** |
| Immutability | LB-generated weekly playlists are immutable; each week is a **new playlist MBID**. Tracks carry `added_at` == playlist creation time, `added_by: "listenbrainz"`. |
| Retention | Weekly playlists carry `expires_at` ≈ creation + 14 days; LB prunes old weeks from `createdfor`. |
| Playlist detail (with tracks) | `GET /1/playlist/{playlist_mbid}` — top-level `{playlist: {...}}`; 50 tracks in `track[]`, **order preserved**. Per track: `identifier[0]` = **recording MBID** (URL), `title` (track name), `creator` (artist name), `album`, `duration` (ms), artist credits with `artist_mbid` + `join_phrase`, cover-art archive IDs. |
| Rate limiting | Max **1 request/second**; responses carry `X-RateLimit-*` headers; exceeding returns `429`. |
| User-Agent | **Required**, format `App-Name/<version> (contact-url-or-email)`. |
| Auth | Weekly Jams are public → **v1 needs no LB token and stores no secrets.** |
| Raw CF recs | `GET /1/cf/recommendation/user/{user}/recording` — experimental, bare MBIDs. Rejected for v1 (see D-1). |

### 2.2 High-level data flow

```mermaid
flowchart LR
    T[Trigger:\nscheduled task or Sync now] --> A[Fetch createdfor\nplaylist list\n(paginated)]
    A --> B[Filter source_patch == weekly-jams,\npick latest by date]
    B --> S{MBID == last\nsynced MBID?}
    S -- yes --> Z[No-op: log up to date\n1 LB request total]
    S -- no --> C[Fetch playlist detail:\nordered recording MBIDs]
    C --> D[Match each recording\nto Jellyfin Audio items\nMBID first, name fallback]
    D --> E{Jellyfin playlist\nexists?}
    E -- no --> F[Create rolling playlist]
    E -- yes --> G[Replace contents in place]
    F --> H[Report:\nmatched / skipped / missing]
    G --> H
```

## 3. Goals & non-goals

**Goals**

1. Zero-touch weekly delivery of Weekly Jams into a Jellyfin playlist, robust against
   LB's generation delays (days or weeks are common).
2. High track-match rate on real libraries (tagged and untagged).
3. Be a good LB API citizen (rate limits, User-Agent) and a good Jellyfin citizen
   (standard plugin patterns: config page, scheduled task, logging).
4. Simple, safe v1 — single LB account, one rolling playlist.

**Non-goals (v1)** — see §10 Out of scope.

## 4. Personas

- **Listener (primary)** — has a ListenBrainz account with listening history; plays music
  from the Jellyfin server. Wants the weekly mix available everywhere Jellyfin is.
- **Server owner (admin)** — installs/configures the plugin, owns the Jellyfin server and
  library. Wants reliable behavior, visible sync results, no server load surprises.

In v1 these are typically the same person (single-account mode, D-2).

## 5. Terminology

| Term | Meaning |
|---|---|
| LB | ListenBrainz (listenbrainz.org) |
| Weekly Jams | LB-generated weekly playlist of familiar tracks (`source_patch: weekly-jams`) |
| Recording MBID | MusicBrainz identifier for a *recording* (a specific audio realization) — what LB playlist tracks reference |
| Release-track MBID | MusicBrainz identifier for a track position within a specific release — **not** usable for LB matching |
| JSPF | JSON Shareable Playlist Format (with MusicBrainz extensions) |
| Rolling playlist | A single Jellyfin playlist whose contents are replaced each sync |
| Created-for-you | LB bot playlists exposed via `…/playlists/createdfor` |

## 6. Scope decisions

Kick-off 2026-10-20; revised same day after verification pass.

| # | Topic | Decision | Rationale |
|---|---|---|---|
| D-1 | Sync sources | **Weekly Jams only** for v1 | Exploration recs surface tracks the listener *doesn't* know → unlikely present in the Jellyfin library. Engine stays source-generic; `source_patch` makes adding sources a config toggle. |
| D-2 | User model | **Single LB account, server-wide**; design so per-Jellyfin-user mapping can be added without rearchitecting | Simplest v1 for personal servers; per-user config UI is v2+ work. |
| D-3 | Playlist lifecycle | **Rolling playlist** refreshed in place | No clutter; playlist is always "this week". Historical mode parked (§14). |
| D-4 | Track matching | **MBID first, name fallback** | Best match rate; works on Picard-tagged libraries, degrades gracefully otherwise. |
| D-5 | Unmatched tracks | **Skip and report** | Playlist stays playable with local tracks; report tells the owner what's missing. No match-rate threshold in v1 (parked). |
| D-6 | Sync triggers | **Scheduled task + manual "Sync now"** | Weekly automation plus on-demand testing. Library-scan hook parked. |
| D-7 | Sync cadence | **Frequent polling — default daily** (04:00, configurable) | LB regenerates Weekly Jams on Monday mornings but delays of days or weeks are common; polling is cheap because of the D-8 short-circuit (1 LB request when unchanged). |
| D-8 | Change detection | **LB playlist MBID is the sync identity**: store the last-synced MBID; if the latest Weekly Jams MBID is unchanged, skip the rest | Leverages immutability of LB-generated playlists (each week = new MBID). Manual "Sync now" from the config page bypasses the short-circuit (force full re-sync). |

## 7. User stories & acceptance criteria

### US-1 — Fresh install, first sync
*As a server owner, I configure my LB username and trigger a sync, so that my Weekly Jams
appear as a Jellyfin playlist.*

- **AC-1.1** Given a valid LB username with an existing Weekly Jams playlist, when a sync
  runs, then a Jellyfin playlist is created containing exactly the matched tracks **in LB
  playlist order** (FR-16, FR-14).
- **AC-1.2** The playlist is created for the configured owning user and visibility
  (FR-03).
- **AC-1.3** Given an unknown/invalid LB username, sync fails with an actionable error
  message and no playlist is created (FR-11).

### US-2 — Weekly refresh
*As a listener, my playlist silently becomes the new week's jams as soon as LB publishes
it.*

- **AC-2.1** Given a previous successful sync, when LB publishes a new Weekly Jams
  playlist and the scheduled task fires, then the **same** Jellyfin playlist contains the
  new week's matched tracks, with no leftovers from the prior week and no duplicates
  (FR-17, NFR-07).
- **AC-2.2** The listener may rename the playlist; the next sync still updates the same
  playlist (FR-16 — tracked by stored ID, not name).
- **AC-2.3** If the owner deletes the playlist, the next sync recreates it (FR-19).
- **AC-2.4** Given the latest Weekly Jams playlist has already been synced (same MBID),
  a scheduled run completes as a no-op — the Jellyfin playlist is not touched (FR-25).

### US-3 — Missing tracks report
*As a server owner, I want to know which recommended tracks I don't have locally.*

- **AC-3.1** After each sync, the log and task result list every unmatched track as
  `artist — title` with counts matched/skipped/total (FR-15, FR-23).

### US-4 — Failure safety
*As a server owner, a failed sync must never wreck my current playlist.*

- **AC-4.1** Given LB returns 5xx/429/timeouts, the sync retries with backoff and either
  succeeds or fails cleanly — the existing playlist is untouched on failure (FR-18, NFR-05).
- **AC-4.2** Given a sync where **zero** tracks match (e.g. library wiped, matcher bug),
  the existing playlist is left unchanged and a warning is logged (FR-18 — zero-match guard).

### US-5 — Manual control
*As a server owner, I want to run a sync on demand and see when it last ran.*

- **AC-5.1** "Sync now" on the plugin config page triggers a **full** sync immediately,
  bypassing the up-to-date short-circuit (FR-22, FR-25).
- **AC-5.2** The config page shows last-sync time and outcome summary, including
  "up to date (week of …)" states (FR-04).

## 8. Functional requirements

Priority: **M** = must (v1), **S** = should, **C** = could.

### 8.1 Configuration

| ID | Pri | Requirement |
|---|---|---|
| FR-01 | M | Plugin config holds the ListenBrainz **username**; sync refuses to run (with clear log) when empty/invalid. |
| FR-02 | M | Config holds the **target playlist name**, default `Weekly Jams`. |
| FR-03 | M | Config holds the **owning Jellyfin user** (default: first admin — OQ-4 resolved) and **visibility** (default: visible to all users). |
| FR-04 | S | Config page provides **"Sync now"** and displays **last sync status** (time, outcome, matched/skipped counts, current week). |
| FR-05 | M | All settings persist via the standard `PluginConfiguration` mechanism (XML, config page edits apply without restart where possible). |

### 8.2 ListenBrainz fetching

| ID | Pri | Requirement |
|---|---|---|
| FR-06 | M | Fetch `createdfor` playlists **with pagination** (follow `count`/`offset` until all pages are read; observed users exceed one page). |
| FR-07 | M | Select the latest Weekly Jams playlist **using structured metadata only**: filter entries on `source_patch == "weekly-jams"`, then pick the entry with the max `date` (creation timestamp). No week-date parsing from titles. Title-prefix match (`Weekly Jams for …`) is permitted only as a defensive fallback when extension metadata is absent. |
| FR-08 | M | Fetch the selected playlist's detail (ordered tracks with recording MBIDs and metadata). |
| FR-09 | M | All LB requests send `User-Agent: Jellyfin.Plugin.ListenBrainzWeeklyJams/<version> (repo URL)`. |
| FR-10 | M | Respect LB rate policy: minimum 1s spacing between requests, honor `X-RateLimit-Reset-In`, exponential backoff on `429`. |
| FR-11 | M | Map LB errors to actionable task failures: 404 → "user not found", no `weekly-jams` entries → "no Weekly Jams playlist yet (new account?)", 5xx/network → retried then failed cleanly. |

### 8.3 Matching (LB recording → Jellyfin Audio)

| ID | Pri | Requirement |
|---|---|---|
| FR-12 | M | **MBID-first**: resolve the LB recording MBID against Jellyfin's `ProviderIds["MusicBrainzRecording"]` (populated from Picard's `musicbrainz_trackid` tag / ID3 UFID — verified, see Appendix A). |
| FR-13 | M | **Name fallback**: match on normalized `artist + title` (case-insensitive, whitespace/punctuation-normalized; "feat." handling). Must not mis-match on empty/blank values. Should use the LB artist MBID against `ProviderIds["MusicBrainzArtist"]` as a tiebreaker when multiple title matches exist. |
| FR-14 | M | Preserve LB playlist order in the Jellyfin playlist; one Jellyfin item per recording; skip duplicate resolves. |
| FR-15 | M | Unmatched recordings are skipped and logged as `artist — title` (+ recording MBID). |

### 8.4 Playlist management

| ID | Pri | Requirement |
|---|---|---|
| FR-16 | M | The plugin **remembers the Jellyfin playlist ID** it creates (plugin data store), and re-uses that playlist on later syncs — never name-based lookup. |
| FR-17 | M | Sync **replaces** playlist contents (build full new item list first, then write — build-then-commit). |
| FR-18 | M | **Zero-match guard**: if the new week resolves to 0 tracks, leave the existing playlist unchanged and log a warning. |
| FR-19 | M | If the remembered playlist was deleted, the next sync recreates it. |
| FR-20 | S | Playlist overview/description records source and week: e.g. `ListenBrainz Weekly Jams — week of 2026-10-19` (week from playlist `date`). |

### 8.5 Scheduling & reporting

| ID | Pri | Requirement |
|---|---|---|
| FR-21 | M | Register an `IScheduledTask` (visible in Dashboard → Scheduled Tasks), default trigger: **daily, 04:00** local server time (D-7: LB regeneration delays make weekly checks too coarse; short-circuit makes daily polling cheap). User-editable like any Jellyfin task. |
| FR-22 | M | Manual trigger: standard "Run now" on the task **and** a Sync-now button on the config page (full re-sync, FR-25). |
| FR-23 | S | Task result summary: outcome (updated / up to date / failed), week date, total/matched/skipped counts, duration. |

### 8.6 Sync identity & change detection

| ID | Pri | Requirement |
|---|---|---|
| FR-24 | M | After a successful sync, persist the source playlist's **LB playlist MBID** (and week date) as last-synced state in plugin data. |
| FR-25 | M | Short-circuit: if the latest Weekly Jams MBID equals the stored one, the sync stops after the list fetch (no detail fetch, no matching, no playlist write) and reports "up to date". Manual "Sync now" from the config page ignores the short-circuit and performs a full re-sync (escape hatch after config changes or library rescans). |

## 9. Non-functional requirements

| ID | Pri | Requirement |
|---|---|---|
| NFR-01 | M | Ships as a standard Jellyfin 10.11 plugin (`net9.0`, `build.yaml` metadata correct, plugin-catalog-ready conventions). |
| NFR-02 | M | Request budget: an up-to-date no-op sync issues **exactly 1** LB request (createdfor pages); a full sync ≤ ~5 (pages + detail). Never violates the 1 rps policy. |
| NFR-03 | S | Matching uses server-side queries (no per-track full-library scans). Target: sync on a 50k-track library completes well under a minute on modest hardware. |
| NFR-04 | M | **No secrets**: v1 stores no LB token; config contains only a public username. |
| NFR-05 | M | Resilience: transient LB/network errors retried with backoff; **no partial writes** — existing playlist state changes only after a fully successful fetch+match. |
| NFR-06 | M | Logging: one summary line per sync (Info), skipped-track list (Info/Warn), failures (Error) with actionable messages. No per-track spam at Info. |
| NFR-07 | M | Idempotent: consecutive syncs of the same week produce an identical playlist (no duplicates, no reordering churn); an unchanged source week results in no playlist write at all (D-8). |
| NFR-08 | M | Plugin starts and logs a warning when config is invalid — never crash-loops the server (sync simply refuses, FR-01). |
| NFR-09 | C | Config UI is English-only in v1. |

## 10. Out of scope (v1)

- Submitting listens / scrobbling Jellyfin plays **to** ListenBrainz (separate concern entirely).
- Weekly Exploration, yearly `top-discoveries-of-{year}` / `top-missed-recordings-of-{year}` playlists (all trivially selectable via `source_patch` — see §14).
- Per-Jellyfin-user LB account mapping (D-2 — v2 candidate).
- Historical (new-playlist-per-week) mode (D-3).
- Match-rate threshold guard / abort (D-5).
- Downloading or acquiring missing tracks.
- i18n of the config page.

## 11. Assumptions & dependencies

1. The LB username's data is public (created-for-you playlists are public per LB docs).
2. LB regenerates Weekly Jams weekly (Monday mornings per user timezone) but **delays of
   days or weeks are common**; daily polling with the MBID short-circuit (D-7/D-8) makes
   the plugin resilient to this without wasted work.
3. Weekly Jams playlists are immutable once published (each week = new MBID). If LB ever
   mutates a playlist in place, the short-circuit could mask it; the config-page force
   sync (FR-25) is the escape hatch.
4. Owner's library is Picard-tagged (recording MBIDs present → strong MBID matching).
   Untagged libraries still work via name fallback, with lower precision.
   **Validated: 97.4% coverage on the owner's server — see Appendix A.3.**
5. The Jellyfin server exposes at least one music library; sync matches across all music libraries.
6. Plugin has server-side access to library, playlist, and task services (standard plugin surface).

## 12. Open questions

All kick-off questions are resolved (see Appendix A for findings):

| # | Question | Status |
|---|---|---|
| OQ-1 | Plugin naming | **Resolved** — "ListenBrainz Weekly Jams" / `Jellyfin.Plugin.ListenBrainzWeeklyJams`. GUID to be minted in M0. |
| OQ-2 | Structured selection fields in `createdfor`? | **Resolved** — verified live: `source_patch` (type) + `date` (creation). No title parsing needed. |
| OQ-3 | MusicBrainz data available/queryable in Jellyfin? | **Resolved** — verified against v10.11.9 source: `MusicBrainzRecording` provider ID + `HasAnyProviderId` server-side filtering. See Appendix A. |
| OQ-4 | Playlist ownership default | **Resolved** — first admin, configurable (FR-03). |

Remaining design-phase questions — all resolved:

| # | Question |
|---|---|
| OQ-5 | **Resolved (2026-10-21)** — matcher uses **paged bulk + streaming MBID-set filter**: page (5k) through all audio items having `MusicBrainzRecording`, keep only candidates matching the playlist's recording-MBID set (all candidates — feeds OQ-6), discard each page, so memory is bounded by page size, not library size. Benchmark at 19k items (`benchmarks/oq5-bench/`, faithful Jellyfin schema + EF shapes): per-track 1,427 ms vs paged bulk 658 ms — the `HasAnyProviderId` concat translation defeats the `(ProviderId, ProviderValue, ItemId)` index, making every per-track query a provider-table scan, and per-track cost additionally scales with playlist length. Name fallback issues per-miss queries only (~2–3 per sync in practice). |
| OQ-6 | **Resolved (2026-10-21)** — minimal deterministic policy, matching the dry-run reference implementation: **exact normalized-title match first** (filters live/remix/edition variants), otherwise **first by Jellyfin item Id** (immutable, never reshuffled by renames). Ambiguous resolves are counted and logged, never hidden. Richer preferences (album vs compilation, quality) parked (§14). |

## 13. Milestones

| # | Milestone | Exit criteria |
|---|---|---|
| M0 | De-template the scaffold | Renamed plugin + minted GUID, `build.yaml`/metadata accurate, CI build green. |
| M1 | LB client | `createdfor` (paginated) + playlist-detail clients with models matching **verified** payload shapes, UA header, rate limiting, error mapping; unit-tested against recorded payloads. |
| M2 | Matcher | MBID matching via `ProviderIds["MusicBrainzRecording"]` + name fallback; unit tests incl. normalization edge cases; resolve OQ-5 with measurements and OQ-6 (ambiguity policy). Dry-run fixtures and baseline numbers available (Appendix A.3). |
| M3 | Sync engine + integration | Playlist create/replace via stored ID, build-then-commit, zero-match guard, MBID short-circuit; scheduled task + config page with Sync now. US-1/2/4/5 demoable. |
| M4 | Polish & v1 release | Last-sync status, logging hygiene, README, changelog, catalog readiness. All §7 ACs verified. |

## 14. Parking lot (explicitly not committed)

- Additional sources as config toggles — cheap now thanks to `source_patch`: Weekly Exploration, `top-discoveries-of-{year}`, `top-missed-recordings-of-{year}`
- Per-user LB mapping; historical playlist mode; threshold guard
- Ambiguity preference options (album over compilation, preferred quality) — superseded by the minimal OQ-6 policy until someone wants them
- Sync-on-library-scan-complete option
- LB token support (private data, higher rate limits)
- "Missing tracks" export (e.g. copyable list) beyond log output

## Appendix A — Verification findings (2026-10-20)

### A.1 ListenBrainz (live calls, production API)

- `GET /1/user/9cxndy/playlists/createdfor`: `playlist_count: 28`, page size 25 → **pagination required**. Each entry carries `date` (creation), `identifier` (MBID URL), `extension["https://musicbrainz.org/doc/jspf#playlist"].algorithm_metadata.source_patch` ∈ {`weekly-jams`, `weekly-exploration`, `top-discoveries-of-YYYY`, `top-missed-recordings-of-YYYY`}, `expires_at` ≈ +14 days.
- `GET /1/playlist/{mbid}` (a `weekly-jams` playlist): 50 ordered tracks; per track: `identifier[0]` = recording MBID URL, `title`, `creator` (artist), `album`, `duration` (ms), artist credits (`artist_mbid`, `join_phrase`), CAA IDs.
- Response shapes differ from readthedocs (no `payload` wrapper): models must be built from recorded payloads (keep fixtures in the repo).

### A.2 Jellyfin v10.11.9 (source inspection)

- `MediaBrowser.Model/Entities/MetadataProvider.cs`: `MusicBrainzTrack = 18`, `MusicBrainzRecording = 20` — **two distinct provider IDs**.
- `MediaBrowser.Providers/MediaInfo/AudioFileProber.cs` (~L421–445), the file-tag path:
  - `MUSICBRAINZ_RELEASETRACKID` tag → `ProviderIds["MusicBrainzTrack"]` (**release-track MBID** — not what LB references)
  - `MUSICBRAINZ_TRACKID` tag → `ProviderIds["MusicBrainzRecording"]` (**recording MBID** — Picard writes the recording MBID here despite the tag name) 
  - plus an ID3v2 **UFID fallback** (owner contains `musicbrainz.org`) → `MusicBrainzRecording`, covering Picard-tagged MP3s
  - Artist/album/release-group MBIDs are likewise populated (`MusicBrainzArtist`, `MusicBrainzAlbumArtist`, `MusicBrainzAlbum`, `MusicBrainzReleaseGroup`)
- `MediaBrowser.Controller/Entities/InternalItemsQuery.cs`: `HasAnyProviderId` (Dictionary<string,string?>), `ExcludeProviderIds`, `NameContains`, `SearchTerm`, `Name` — all plugin-usable via `ILibraryManager`.
- `Jellyfin.Server.Implementations/Item/BaseItemRepository.cs` (~L2442–2462): `HasAnyProviderId` translates to server-side EF queries on the normalized provider-mapping table. An **empty value** means "any item having this provider set" (useful for a one-shot bulk pull of all recording-tagged audio items); a key+value pair means exact `ProviderId:Value` matching.
- Consequence: **MBID matching is first-class** — `HasAnyProviderId { "MusicBrainzRecording": "<mbid>" }` per track, or one bulk query for all items with the provider set. Decision deferred to design doc (OQ-5).
- Caveat: provider IDs come from **file tags at scan time** (AudioFileProber / ffprobe), not from Jellyfin's MusicBrainz album metadata providers — so library must have been scanned after tagging.

### A.3 Dry run against the owner's account + server (2026-10-20)

Executed `scripts/dry_run.py` (read-only) against LB user `tabjy` and their Jellyfin
server (19,061 audio items), syncing the *week of 2026-10-12* jams playlist:

| Metric | Result |
|---|---|
| Match rate | **48/50 (96%)** — 45 via MBID, 3 via name fallback |
| Missing (not in library) | 2 → reported per D-5 |
| Recording-MBID tagging coverage | **97.4%** (18,564/19,061) — assumption 4 validated |
| Distinct recording MBIDs | 17,920 (644 items share an MBID with another item) |
| Ambiguous track resolves | **11/50 (22%)** → OQ-6 |
| Bulk library fetch (HTTP, 10 pages) | 17.6s for 19k items |
| LB requests per full sync | 2 (list page + detail) |

Additional observations:

- **Same-week regeneration observed**: two `weekly-jams` playlists for the week of
  2026-10-12, created 69 s apart, only 52% track overlap. Latest-by-`date` (FR-07)
  selects the newer one; a mid-week regeneration issues a **new MBID**, which correctly
  bypasses the D-8 short-circuit and re-syncs. No special-casing needed.
- **CDN in front of Jellyfin**: the server's public URL sits behind Cloudflare, which
  blocks default library User-Agents (error 1010) while accepting a descriptive UA.
  The plugin is unaffected (in-process, never traverses the CDN), but any HTTP-based
  tooling must send a proper User-Agent — FR-09-style UA hygiene applies to Jellyfin
  clients too.
- CJK normalization was a non-factor for matching on this library: MBID resolution
  carried 94% of matches; name-fallback rescues worked on first-name-tier matches.

## Change log

| Version | Date | Changes |
|---|---|---|
| v0.1 | 2026-10-20 | Initial draft from kick-off Q&A. |
| v0.2 | 2026-10-20 | D-7/D-8 added (daily cadence + immutable-MBID short-circuit); FR-07 selection via structured metadata (`source_patch` + `date`); FR-21 default daily; §8.6 added (FR-24/25); pagination mandatory (FR-06); OQ-1…4 resolved with live API + Jellyfin source verification (Appendix A); OQ-5 added for design phase. |
| v0.3 | 2026-10-20 | Dry-run results recorded (Appendix A.3): 96% match rate, 97.4% tagging coverage, 22% ambiguity rate → OQ-6 added; same-week regeneration and CDN/UA findings; assumption 4 validated. |
| v0.4 | 2026-10-21 | OQ-5 resolved by measurement (`benchmarks/oq5-bench/`): paged bulk + streaming MBID-set filter beats per-track 658 ms vs 1,427 ms at 19k items; concat predicate in `HasAnyProviderId` translation defeats the provider index. |
| v0.5 | 2026-10-21 | OQ-6 resolved: minimal deterministic ambiguity policy — exact normalized-title match first, then lowest Jellyfin item Id; ambiguities counted and logged. Dry-run reference implementation and tests aligned. All design-phase questions resolved; design doc unblocked. |
| v0.6 | 2026-10-21 | Target platform corrected: 10.11/net9.0 → **12.2/net10.0**. Integration pre-flight (server `/System/Info/Public`) revealed the deployment server runs 12.2.0 — the plugin template's 10.11 default was stale. All load-bearing API facts re-verified against `v12.2` source: UpdatePlaylist replace/null-Name semantics, plugin MVC application parts, `MediaBrowser.Common.Api` policies, `HasAnyProviderId` empty-value matching, MusicBrainzRecording tag mapping — unchanged. Delta: `AddItemToPlaylistAsync` gained `int? position`; new batched `HasAnyProviderIds` noted as future optimization. |
