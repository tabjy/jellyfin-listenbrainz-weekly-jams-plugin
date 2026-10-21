#!/usr/bin/env python3
"""Dry-run for the ListenBrainz Weekly Jams -> Jellyfin sync strategy.

Validates the plan end-to-end against real data, completely read-only:

  1. LB fetch path: createdfor (paginated) -> filter source_patch == weekly-jams
     -> select latest by creation `date` (FR-06/07)
  2. D-8 short-circuit: state file; when the latest playlist MBID was already
     synced, stops after the list fetch (FR-24/25). --force bypasses it.
  3. Playlist detail fetch: ordered tracks with recording MBIDs (FR-08)
  4. Matching: ProviderIds["MusicBrainzRecording"] first (FR-12), then
     artist+title fallback with artist-MBID tiebreak (FR-13)
  5. Report: match rate, tagging coverage, ambiguities, and the ordered list
     of Jellyfin items the plugin would put in the playlist (FR-14)

No playlist is created or modified. Jellyfin access is read-only (GET /Items).

Usage:
  ./scripts/dry_run.py --lb-user <name> --jf-url http://host:8096 --jf-key <apikey>

  Credentials can also come from env vars (or /tmp/opencode/lbjf-dryrun.env,
  which is loaded automatically if present):
      LB_USER=...      ListenBrainz username (public data, no token needed)
      JF_URL=...       Jellyfin base URL
      JF_API_KEY=...   Jellyfin API key (Dashboard -> API Keys)

Options:
  --force               bypass the up-to-date short-circuit (full re-sync)
  --state PATH          state file (default: /tmp/opencode/lbjf-dryrun-state.json)
  --save-fixtures DIR   dump raw LB JSON responses as M1 test fixtures
  --report-json PATH    also write the summary as JSON
  --lb-api-base URL     override LB API root (default: https://api.listenbrainz.org)

Note: Jellyfin API keys grant full admin API access. Use a fresh key for the
dry run and revoke it afterwards.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field

LB_API_BASE = "https://api.listenbrainz.org"
UA = ("Jellyfin.Plugin.ListenBrainzWeeklyJams-dryrun/0.1 "
      "(https://github.com/tabjy/jellyfin-listenbrainz-recommendations-plugin)")
JSPF_EXT = "https://musicbrainz.org/doc/jspf#playlist"
JSPF_TRACK_EXT = "https://musicbrainz.org/doc/jspf#track"
JF_PAGE_SIZE = 2000
LB_MIN_INTERVAL = 1.0  # LB policy: max 1 request/second

ENV_FILE = "/tmp/opencode/lbjf-dryrun.env"


# --------------------------------------------------------------------------- #
# Small helpers
# --------------------------------------------------------------------------- #

def log(msg: str = "") -> None:
    print(msg, flush=True)


def die(code: int, msg: str) -> "None":
    print(f"\nERROR: {msg}", file=sys.stderr)
    sys.exit(code)


def load_env_file(path: str) -> None:
    """Tiny .env loader; never prints values."""
    if not os.path.isfile(path):
        return
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, _, value = line.partition("=")
            os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


def _fold_compat(s: str) -> str:
    """Fold compatibility forms (full-width -> ASCII, ligatures) and Latin
    diacritics (é -> e), but preserve kana voicing marks: under NFKD, で
    decomposes to て + combining dakuten, and stripping combining marks would
    wrongly collapse voiced/unvoiced kana (で == て). Kana stay precomposed.
    """
    out = []
    for ch in s:
        d = unicodedata.normalize("NFKD", ch)
        if len(d) == 1:
            out.append(d)  # e.g. Ａ -> A, 　 -> space
        elif all(unicodedata.combining(c) for c in d[1:]):
            base = d[0]
            # diacritic decorates a Latin base -> fold; anything else (kana
            # dakuten/handakuten) -> keep the original precomposed character
            out.append(base if (base.isascii() and base.isalpha()) else ch)
        else:
            out.append(d)  # e.g. ligature ﬁ -> fi
    return "".join(out)


def norm(s: str) -> str:
    """Normalize a name for comparison: case, punctuation, feat-stripping."""
    if not s:
        return ""
    s = _fold_compat(s)
    s = s.casefold()
    s = re.sub(r"\b(feat|ft|featuring)\b\.?\s.*", " ", s)
    s = re.sub(r"[^\w\s]", " ", s)  # \w is unicode-aware
    return re.sub(r"\s+", " ", s).strip()


def norm_strip_brackets(s: str) -> str:
    """norm() with bracketed qualifiers removed, e.g. '(remastered)'."""
    return norm(re.sub(r"[(\[].*?[)\]]", " ", s or ""))


def mbid_from_url(url: str) -> str:
    return url.rstrip("/").rsplit("/", 1)[-1].lower() if url else ""


# --------------------------------------------------------------------------- #
# ListenBrainz client (rate-limited, per LB policy)
# --------------------------------------------------------------------------- #

class LbClient:
    def __init__(self, base: str, fixture_dir: str | None = None):
        self.base = base.rstrip("/")
        self._last_request = 0.0
        self.fixture_dir = fixture_dir
        self.request_count = 0

    def get(self, path: str, params: dict | None = None, fixture_name: str | None = None) -> dict:
        url = self.base + path
        if params:
            url += "?" + urllib.parse.urlencode(params)

        for attempt in range(5):
            wait = LB_MIN_INTERVAL - (time.monotonic() - self._last_request)
            if wait > 0:
                time.sleep(wait)

            req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/json"})
            try:
                self._last_request = time.monotonic()
                self.request_count += 1
                with urllib.request.urlopen(req, timeout=30) as resp:
                    data = json.load(resp)
                if self.fixture_dir and fixture_name:
                    os.makedirs(self.fixture_dir, exist_ok=True)
                    with open(os.path.join(self.fixture_dir, fixture_name), "w", encoding="utf-8") as fh:
                        json.dump(data, fh, indent=2, ensure_ascii=False)
                return data
            except urllib.error.HTTPError as e:
                if e.code == 429:
                    reset = float(e.headers.get("X-RateLimit-Reset-In", "1"))
                    log(f"  LB 429: backing off {reset + 0.5:.1f}s (rate limit)")
                    time.sleep(reset + 0.5)
                    continue
                if 500 <= e.code < 600 and attempt < 4:
                    backoff = 2 ** attempt
                    log(f"  LB {e.code}: retrying in {backoff}s")
                    time.sleep(backoff)
                    continue
                die(2, f"ListenBrainz {e.code} on {path}: {e.read().decode(errors='replace')[:200]}")
            except urllib.error.URLError as e:
                if attempt < 4:
                    backoff = 2 ** attempt
                    log(f"  LB network error ({e.reason}); retrying in {backoff}s")
                    time.sleep(backoff)
                    continue
                die(2, f"ListenBrainz unreachable: {e}")
        die(2, "ListenBrainz: retries exhausted")  # unreachable


def lb_fetch_createdfor(lb: LbClient, user: str) -> list[dict]:
    """All created-for-you playlists, following pagination."""
    playlists: list[dict] = []
    offset = 0
    while True:
        page = lb.get(f"/1/user/{urllib.parse.quote(user)}/playlists/createdfor",
                      {"offset": offset},
                      fixture_name=f"createdfor-offset-{offset}.json")
        # Live shape (verified 2026-10-20) has no wrapper; be defensive anyway.
        body = page.get("payload", page)
        entries = body.get("playlists", [])
        playlists.extend(e["playlist"] for e in entries if "playlist" in e)
        total = body.get("playlist_count", len(playlists))
        offset += len(entries)
        if not entries or offset >= total:
            break
    return playlists


def lb_select_weekly_jams(playlists: list[dict], user: str) -> tuple[dict, list[dict]]:
    """Filter source_patch == weekly-jams, pick latest by creation date (FR-07)."""
    jams = []
    for p in playlists:
        ext = (p.get("extension") or {}).get(JSPF_EXT) or {}
        src = ((ext.get("additional_metadata") or {}).get("algorithm_metadata") or {}).get("source_patch")
        if src == "weekly-jams":
            jams.append(p)
    if not jams:
        die(2, f"no weekly-jams playlists in createdfor for '{user}' "
               "(new account? listening history too short?)")
    latest = max(jams, key=lambda p: p.get("date") or "")
    return latest, jams


def lb_fetch_playlist(lb: LbClient, mbid: str) -> dict:
    data = lb.get(f"/1/playlist/{mbid}", fixture_name="playlist-detail.json")
    return data.get("playlist", data)


@dataclass
class LbTrack:
    position: int
    recording_mbid: str
    title: str
    artist: str
    album: str
    artist_mbids: list[str] = field(default_factory=list)


def lb_extract_tracks(playlist: dict) -> list[LbTrack]:
    tracks: list[LbTrack] = []
    for i, t in enumerate(playlist.get("track") or [], start=1):
        ext = (t.get("extension") or {}).get(JSPF_TRACK_EXT) or {}
        artists = ((ext.get("additional_metadata") or {}).get("artists")) or []
        ids = t.get("identifier") or []
        tracks.append(LbTrack(
            position=i,
            recording_mbid=mbid_from_url(ids[0]) if ids else "",
            title=t.get("title") or "",
            artist=t.get("creator") or "",
            album=t.get("album") or "",
            artist_mbids=[a.get("artist_mbid", "").lower() for a in artists if a.get("artist_mbid")],
        ))
    return tracks


# --------------------------------------------------------------------------- #
# Jellyfin client (read-only)
# --------------------------------------------------------------------------- #

class JfClient:
    def __init__(self, base: str, api_key: str):
        self.base = base.rstrip("/")
        self.headers = {
            "Authorization": f'MediaBrowser Token="{api_key}"',
            "Accept": "application/json",
            # default urllib UA is blocked by some CDN/bot-protection layers
            # in front of public Jellyfin endpoints (observed: Cloudflare 1010)
            "User-Agent": UA,
        }

    def _get(self, path: str, params: dict) -> dict:
        url = f"{self.base}{path}?{urllib.parse.urlencode(params)}"
        req = urllib.request.Request(url, headers=self.headers)
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                return json.load(resp)
        except urllib.error.HTTPError as e:
            die(3, f"Jellyfin {e.code} on {path}: {e.read().decode(errors='replace')[:200]}")
        except urllib.error.URLError as e:
            die(3, f"Jellyfin unreachable at {self.base}: {e}")

    def fetch_all_audio(self) -> list[dict]:
        """All audio items across libraries, with ProviderIds (bulk strategy)."""
        items: list[dict] = []
        start = 0
        while True:
            page = self._get("/Items", {
                "includeItemTypes": "Audio",
                "recursive": "true",
                "isVirtualItem": "false",
                "fields": "ProviderIds",
                "enableImages": "false",
                "enableUserData": "false",
                "sortBy": "SortName",
                "startIndex": start,
                "limit": JF_PAGE_SIZE,
            })
            batch = page.get("Items", [])
            items.extend(batch)
            total = page.get("TotalRecordCount", len(items))
            start += len(batch)
            log(f"  Jellyfin: {min(start, total)}/{total} audio items")
            if not batch or start >= total:
                break
        return items


# --------------------------------------------------------------------------- #
# Matching
# --------------------------------------------------------------------------- #

@dataclass
class MatchResult:
    strategy: str  # MBID | NAME | NAME-B | MISS
    ambiguous: bool
    item: dict | None
    note: str = ""


def build_maps(jf_items: list[dict]) -> tuple[dict, dict, dict, dict]:
    """mbid -> [items], (artist, title) maps (exact and bracket-stripped), stats."""
    mbid_map: dict[str, list[dict]] = {}
    name_map: dict[tuple[str, str], list[dict]] = {}
    name_map_b: dict[tuple[str, str], list[dict]] = {}
    with_recording = 0
    for it in jf_items:
        pids = it.get("ProviderIds") or {}
        rec = (pids.get("MusicBrainzRecording") or "").lower()
        if rec:
            with_recording += 1
            mbid_map.setdefault(rec, []).append(it)
        title = it.get("Name") or ""
        artists = set(norm(a) for a in (it.get("Artists") or []) if norm(a))
        artists |= set(norm(a.get("Name", "")) for a in (it.get("AlbumArtists") or []) if norm(a.get("Name", "")))
        for a in artists:
            for key, mp in (((a, norm(title)), name_map),
                            ((norm_strip_brackets(a), norm_strip_brackets(title)), name_map_b)):
                bucket = mp.setdefault(key, [])
                if it not in bucket:
                    bucket.append(it)
    stats = {
        "audio_items": len(jf_items),
        "items_with_recording_mbid": with_recording,
        "distinct_recording_mbids": len(mbid_map),
        "tagging_coverage_pct": round(100.0 * with_recording / len(jf_items), 1) if jf_items else 0.0,
    }
    return mbid_map, name_map, name_map_b, stats


def match_track(track: LbTrack, mbid_map: dict, name_map: dict, name_map_b: dict) -> MatchResult:
    # --- FR-12: MBID first -------------------------------------------------- #
    if track.recording_mbid:
        candidates = mbid_map.get(track.recording_mbid) or []
        if candidates:
            ambiguous = len(candidates) > 1
            item = pick_candidate(candidates, track)
            note = f"{len(candidates)} item(s) share this recording MBID" if ambiguous else ""
            return MatchResult("MBID", ambiguous, item, note)

    # --- FR-13: name fallback ----------------------------------------------- #
    # T1: exact normalized (artist, title); T2: bracket-stripped (e.g. ignores
    # "(remastered 2011)"); T3: bracket-stripped title + artist containment.
    candidates = name_map.get((norm(track.artist), norm(track.title))) or []
    tier = "NAME" if candidates else None

    if not candidates:
        nb_key = (norm_strip_brackets(track.artist), norm_strip_brackets(track.title))
        candidates = name_map_b.get(nb_key) or []
        tier = "NAME-B" if candidates else None

    if not candidates:
        nb_artist = norm_strip_brackets(track.artist)
        nb_title = norm_strip_brackets(track.title)
        if nb_title:
            seen: dict[str, dict] = {}
            for (a, t), its in name_map_b.items():
                if t == nb_title and nb_artist and (a in nb_artist or nb_artist in a):
                    for it in its:
                        seen.setdefault(it["Id"], it)
            candidates = list(seen.values())
        tier = "NAME-B" if candidates else tier

    if not candidates:
        return MatchResult("MISS", False, None, "")

    # tiebreak: artist MBID (FR-13 should), then exact-artist equality
    mbid_hits = [c for c in candidates
                 if ((c.get("ProviderIds") or {}).get("MusicBrainzArtist") or "").lower() in track.artist_mbids]
    if mbid_hits:
        candidates = mbid_hits
    ambiguous = len(candidates) > 1
    item = pick_candidate(candidates, track)
    return MatchResult(tier, ambiguous, item,
                       f"{len(candidates)} name candidates" if ambiguous else "")


def pick_candidate(candidates: list[dict], track: LbTrack) -> dict:
    """OQ-6 ambiguity policy: exact normalized-title match first, then
    deterministic order by Jellyfin item Id."""
    if len(candidates) == 1:
        return candidates[0]
    exact = [c for c in candidates if norm(c.get("Name") or "") == norm(track.title)]
    return sorted(exact or candidates, key=lambda c: c.get("Id") or "")[0]


# --------------------------------------------------------------------------- #
# Report
# --------------------------------------------------------------------------- #

def item_label(item: dict) -> str:
    artists = item.get("Artists") or [a.get("Name", "?") for a in item.get("AlbumArtists") or []]
    return f"{item.get('Name', '?')} — {artists[0] if artists else '?'}"


def save_state(path: str, latest_mbid: str, summary: dict) -> None:
    state: dict = {}
    if os.path.isfile(path):
        try:
            state = json.load(open(path, encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            state = {}
    state.update({"last_mbid": latest_mbid, "last_run": time.strftime("%Y-%m-%dT%H:%M:%S"),
                  "last_summary": summary})
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(state, fh, indent=2)
    log(f"State saved to {path} (re-run to see the FR-25 short-circuit fire)")


def run(args: argparse.Namespace) -> int:
    t0 = time.monotonic()
    lb = LbClient(args.lb_api_base, args.save_fixtures)

    # ---- Phase 1: createdfor list + selection ------------------------------ #
    log(f"[1/4] ListenBrainz: fetching created-for-you playlists for '{args.lb_user}'")
    playlists = lb_fetch_createdfor(lb, args.lb_user)
    log(f"      {len(playlists)} playlists total ({lb.request_count} request(s))")

    latest, all_jams = lb_select_weekly_jams(playlists, args.lb_user)
    latest_mbid = mbid_from_url(latest["identifier"])
    log(f"      weekly-jams playlists on record: {len(all_jams)}")
    for j in sorted(all_jams, key=lambda p: p.get("date") or "", reverse=True):
        log(f"        - {j.get('date', '?')[:10]}  {mbid_from_url(j['identifier'])}")
    log(f"      SELECTED: {latest['title']}")
    log(f"                 date={latest.get('date')}  mbid={latest_mbid}")

    # ---- Phase 2: D-8 short-circuit demo ----------------------------------- #
    state = {}
    if os.path.isfile(args.state):
        try:
            state = json.load(open(args.state, encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            state = {}
    if state.get("last_mbid") == latest_mbid and not args.force:
        log("\n[2/4] UP TO DATE — short-circuit fires here (FR-25).")
        log(f"      {latest_mbid} was already synced on {state.get('last_run', '?')}.")
        log(f"      Spent so far: {lb.request_count} LB request(s) for the list phase. Nothing else would happen.")
        log("      (Use --force to simulate a config-page 'Sync now'.)")
        return 0
    log(f"\n[2/4] Change detection: {'forced (--force)' if args.force else 'new week detected' if state else 'first run'}"
        f" → full sync proceeds")

    # ---- Phase 3: playlist detail ------------------------------------------ #
    log(f"\n[3/4] ListenBrainz: fetching playlist detail {latest_mbid}")
    playlist = lb_fetch_playlist(lb, latest_mbid)
    tracks = lb_extract_tracks(playlist)
    log(f"      {len(tracks)} tracks, order preserved; LB requests so far: {lb.request_count}")
    if not tracks:
        die(2, "playlist has no tracks — nothing to sync")

    if not args.jf_url or not args.jf_key:
        log("\n[4/4] No Jellyfin configured — stopping after LB phases.")
        log("      Re-run with --jf-url/--jf-key (or JF_URL/JF_API_KEY) to test matching.")
        for t in tracks[:10]:
            log(f"        sample: {t.artist} — {t.title} [{t.recording_mbid or 'no mbid'}]")
        summary = {
            "playlist_mbid": latest_mbid,
            "playlist_title": latest["title"],
            "playlist_date": latest.get("date"),
            "total": len(tracks),
            "lb_only": True,
            "lb_requests": lb.request_count,
        }
        save_state(args.state, latest_mbid, summary)
        return 0

    # ---- Phase 4: Jellyfin fetch + matching -------------------------------- #
    jf = JfClient(args.jf_url, args.jf_key)
    log(f"\n[4/4] Jellyfin: fetching audio items from {args.jf_url}")
    t_jf = time.monotonic()
    items = jf.fetch_all_audio()
    jf_secs = time.monotonic() - t_jf
    mbid_map, name_map, name_map_b, stats = build_maps(items)
    log(f"      {stats['audio_items']} audio items in {jf_secs:.1f}s")
    log(f"      recording-MBID tagging coverage: "
        f"{stats['items_with_recording_mbid']}/{stats['audio_items']} ({stats['tagging_coverage_pct']}%)")
    log(f"      distinct recording MBIDs: {stats['distinct_recording_mbids']}")

    results: list[tuple[LbTrack, MatchResult]] = []
    for t in tracks:
        results.append((t, match_track(t, mbid_map, name_map, name_map_b)))

    # ---- Report ------------------------------------------------------------ #
    matched = [r for _, r in results if r.item]
    by_mb = [r for _, r in results if r.strategy == "MBID"]
    by_name = [r for _, r in results if r.strategy.startswith("NAME")]
    missing = [(t, r) for t, r in results if r.strategy == "MISS"]
    amb = [r for _, r in results if r.ambiguous]

    log("\n" + "=" * 78)
    log("PER-TRACK RESULTS")
    log("=" * 78)
    for t, r in results:
        mark = {"MBID": "MBID", "NAME": "NAME", "NAME-B": "NAME*", "MISS": "MISS"}[r.strategy]
        target = item_label(r.item) if r.item else "—"
        log(f"  {t.position:>2}. [{mark:<4}] {t.artist} — {t.title}")
        log(f"       {' ' * 4}  -> {target}" + (f"   ({r.note})" if r.note else ""))

    log("\n" + "=" * 78)
    log("SUMMARY")
    log("=" * 78)
    log(f"  Playlist          : {latest['title']}")
    log(f"  Total tracks      : {len(tracks)}")
    log(f"  Matched           : {len(matched)} ({100.0 * len(matched) / len(tracks):.0f}%)")
    log(f"    via MBID        : {len(by_mb)}")
    log(f"    via name        : {len(by_name)}")
    log(f"  Missing           : {len(missing)}")
    log(f"  Ambiguous resolves: {len(amb)}")
    log(f"  Library coverage  : {stats['tagging_coverage_pct']}% of audio items carry MusicBrainzRecording")
    log(f"  LB requests       : {lb.request_count}   JF fetch: {jf_secs:.1f}s   "
        f"total wall: {time.monotonic() - t0:.1f}s")

    if missing:
        log("\nMISSING TRACKS (artist — title):")
        for t, _ in missing:
            log(f"  - {t.artist} — {t.title}")

    log("\nSIMULATED PLAYLIST (what the plugin would create, in order):")
    for i, (_, r) in enumerate((res for res in results if res[1].item), start=1):
        log(f"  {i:>2}. {item_label(r.item)}   [id={r.item['Id']}]")

    # ---- State (FR-24 demo) ------------------------------------------------ #
    summary = {
        "playlist_mbid": latest_mbid,
        "playlist_title": latest["title"],
        "playlist_date": latest.get("date"),
        "total": len(tracks),
        "matched": len(matched),
        "via_mbid": len(by_mb),
        "via_name": len(by_name),
        "missing": len(missing),
        "tagging_coverage_pct": stats["tagging_coverage_pct"],
        "lb_requests": lb.request_count,
    }
    if args.report_json:
        with open(args.report_json, "w", encoding="utf-8") as fh:
            json.dump(summary, fh, indent=2)
        log(f"\nSummary JSON written to {args.report_json}")
    save_state(args.state, latest_mbid, summary)
    return 0


def main() -> None:
    load_env_file(ENV_FILE)
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lb-user", default=os.environ.get("LB_USER"))
    ap.add_argument("--lb-api-base", default=os.environ.get("LB_API_BASE", LB_API_BASE))
    ap.add_argument("--jf-url", default=os.environ.get("JF_URL"))
    ap.add_argument("--jf-key", default=os.environ.get("JF_API_KEY"))
    ap.add_argument("--force", action="store_true", help="bypass the up-to-date short-circuit")
    ap.add_argument("--state", default="/tmp/opencode/lbjf-dryrun-state.json")
    ap.add_argument("--save-fixtures", metavar="DIR", help="dump raw LB responses as fixtures")
    ap.add_argument("--report-json", metavar="PATH", help="write summary JSON")
    args = ap.parse_args()

    if not args.lb_user:
        die(1, "LB username required (--lb-user or LB_USER env)")
    if (args.jf_url is None) != (args.jf_key is None):
        die(1, "--jf-url and --jf-key must be provided together (or neither)")
    sys.exit(run(args))


if __name__ == "__main__":
    main()
