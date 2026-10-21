#!/usr/bin/env python3
"""Unit tests for the dry-run matcher logic (scripts/dry_run.py).

These test the pure functions — build_maps(), match_track(), norm() — against
synthetic Jellyfin item DTOs and synthetic LB tracks. They are the seed of the
M2 plugin unit tests: same cases, ported to C#.

Run:  python3 -m unittest discover -s scripts -p 'test_*.py' -v
"""

from __future__ import annotations

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import dry_run as dr  # noqa: E402

MBID = "11111111-1111-1111-1111-111111111111"
DUP_MBID = "55555555-5555-5555-5555-555555555555"
ARTIST_MBID = "aaaa0000-0000-0000-0000-000000000001"


def make_library() -> list[dict]:
    return [
        # MBID-tagged, single item
        {"Id": "item-1", "Name": "Track One", "Artists": ["Artist A"],
         "ProviderIds": {"MusicBrainzRecording": MBID, "MusicBrainzArtist": ARTIST_MBID}},
        # untagged, plain name match
        {"Id": "item-2", "Name": "Track Two", "Artists": ["Artist B"], "ProviderIds": {}},
        # bracketed title
        {"Id": "item-3", "Name": "Track Three (Remastered 2011)", "Artists": ["Artist C"], "ProviderIds": {}},
        # artist stored without article
        {"Id": "item-4", "Name": "Yesterday", "Artists": ["Beatles"], "ProviderIds": {}},
        # two items sharing one recording MBID (album + compilation)
        {"Id": "item-5a", "Name": "Track Five", "Artists": ["Artist E"],
         "ProviderIds": {"MusicBrainzRecording": DUP_MBID}},
        {"Id": "item-5b", "Name": "Track Five", "Artists": ["Artist E"],
         "ProviderIds": {"MusicBrainzRecording": DUP_MBID}},
    ]


class MatcherTestCase(unittest.TestCase):
    def setUp(self):
        self.mbid_map, self.name_map, self.name_map_b, self.stats = dr.build_maps(make_library())

    def match(self, **kwargs):
        fields = {"position": 1, "recording_mbid": "", "title": "", "artist": "", "album": "",
                  "artist_mbids": []}
        fields.update(kwargs)
        return dr.match_track(dr.LbTrack(**fields), self.mbid_map, self.name_map, self.name_map_b)

    # --- FR-12: MBID first --------------------------------------------------

    def test_mbid_exact(self):
        r = self.match(recording_mbid=MBID, title="Track One", artist="Artist A")
        self.assertEqual(r.strategy, "MBID")
        self.assertEqual(r.item["Id"], "item-1")
        self.assertFalse(r.ambiguous)

    def test_mbid_ambiguity_flagged(self):
        r = self.match(recording_mbid=DUP_MBID, title="Track Five", artist="Artist E")
        self.assertEqual(r.strategy, "MBID")
        self.assertTrue(r.ambiguous)
        self.assertIn(r.item["Id"], {"item-5a", "item-5b"})

    # --- FR-13: name fallback tiers -----------------------------------------

    def test_t1_exact_normalized(self):
        r = self.match(title="track two", artist="ARTIST B")  # case differs
        self.assertEqual(r.strategy, "NAME")
        self.assertEqual(r.item["Id"], "item-2")

    def test_t2_bracket_stripping(self):
        r = self.match(title="Track Three", artist="Artist C")  # item has "(Remastered 2011)"
        self.assertEqual(r.strategy, "NAME-B")
        self.assertEqual(r.item["Id"], "item-3")

    def test_t3_artist_containment(self):
        # "The Beatles" (LB) vs "Beatles" (library): article difference only
        r = self.match(title="Yesterday", artist="The Beatles")
        self.assertEqual(r.strategy, "NAME-B")
        self.assertEqual(r.item["Id"], "item-4")

    def test_feat_collapses_to_t1(self):
        # norm() strips "feat. ..." so this resolves at the exact tier, not T3
        r = self.match(title="Track Two", artist="Artist B feat. Someone")
        self.assertEqual(r.strategy, "NAME")
        self.assertEqual(r.item["Id"], "item-2")

    def test_miss_on_wrong_title(self):
        r = self.match(title="No Such Song", artist="Artist A")
        self.assertEqual(r.strategy, "MISS")
        self.assertIsNone(r.item)

    def test_artist_mbid_tiebreak(self):
        items = [
            {"Id": "cover", "Name": "Same Song", "Artists": ["Cover Band"], "ProviderIds": {}},
            {"Id": "orig", "Name": "Same Song", "Artists": ["Orig Artist"],
             "ProviderIds": {"MusicBrainzArtist": ARTIST_MBID}},
        ]
        mbid_map, name_map, name_map_b, _ = dr.build_maps(items)
        track = dr.LbTrack(1, "", "Same Song", "Orig Artist", "X",
                           artist_mbids=[ARTIST_MBID])
        r = dr.match_track(track, mbid_map, name_map, name_map_b)
        self.assertEqual(r.item["Id"], "orig")

    # --- normalization ------------------------------------------------------

    def test_norm(self):
        self.assertEqual(dr.norm("Hatsune Miku"), "hatsune miku")
        self.assertEqual(dr.norm("hatsune  miku!"), "hatsune miku")
        self.assertEqual(dr.norm("HATSUNE MIKU (feat. GUMI)"), "hatsune miku")
        self.assertEqual(dr.norm("Café Blue"), "cafe blue")  # Latin diacritics folded

    def test_norm_cjk(self):
        # CJK punctuation -> spaces, collapsed
        self.assertEqual(dr.norm("25時、ナイトコードで。"), "25時 ナイトコードで")
        # Latin letter inside a kana name still casefolds
        self.assertEqual(dr.norm("みきとP"), "みきとp")
        # full-width Latin + ideographic space fold
        self.assertEqual(dr.norm("Ｈａｔｓｕｎｅ　Ｍｉｋｕ"), "hatsune miku")
        # feat-stripping works with CJK around it (space-separated)
        self.assertEqual(dr.norm("曲 feat. 宵崎奏"), "曲")
        # pure CJK passthrough
        self.assertEqual(dr.norm("少女レイ"), "少女レイ")

    def test_norm_dakuten_preserved(self):
        # Regression: NFKD + blanket combining-strip collapsed voiced kana
        # (で -> て), causing false matches between different words.
        self.assertNotEqual(dr.norm("で"), dr.norm("て"))
        self.assertNotEqual(dr.norm("ド"), dr.norm("ト"))
        self.assertNotEqual(dr.norm("ばら"), dr.norm("はら"))

    def test_cjk_t1_match(self):
        # Real-world shape from the captured LB fixture: LB and Jellyfin both
        # store the same CJK spellings -> resolves at the exact tier.
        items = [{"Id": "jp-1", "Name": "少女レイ", "Artists": ["みきとP"], "ProviderIds": {}}]
        mbid_map, name_map, name_map_b, _ = dr.build_maps(items)
        r = dr.match_track(dr.LbTrack(1, "", "少女レイ", "みきとP", "DAISAN WAVE"),
                           mbid_map, name_map, name_map_b)
        self.assertEqual(r.strategy, "NAME")
        self.assertEqual(r.item["Id"], "jp-1")

    def test_norm_strip_brackets(self):
        self.assertEqual(dr.norm_strip_brackets("Song (Live)"), "song")
        self.assertEqual(dr.norm_strip_brackets("Song [Demo]"), "song")

    # --- stats ---------------------------------------------------------------

    def test_tagging_coverage_stats(self):
        self.assertEqual(self.stats["audio_items"], 6)
        self.assertEqual(self.stats["items_with_recording_mbid"], 3)
        self.assertEqual(self.stats["distinct_recording_mbids"], 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
