"""Tests for build_lexicon.py. Run from the repo root:
    uv run python -m unittest scripts/test_build_lexicon.py
Network-free: a tiny hand-written infl.txt and lemma set, and file:// URLs for fetch()."""

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import build_lexicon as bl  # noqa: E402  (the path insert above must come first)

# word -> smallest SCOWL size. 'went', 'cats' and 'worse' are in the list as SCOWL's own lists
# carry inflected forms; 'better' too. 'zyzzyva' is deliberately absent.
LEMMA_SIZES = {
    'find': 10, 'found': 35, 'go': 10, 'went': 10, 'gone': 10, 'lie': 10, 'lay': 10,
    'cat': 10, 'cats': 10, 'dream': 20, 'dive': 35, 'spell': 20, 'wake': 20, 'learn': 10,
    'burn': 10, 'get': 10, 'good': 20, 'well': 10, 'better': 20, 'bad': 10, 'ill': 10,
    'worse': 20, 'the': 10,
}

# Marker syntax below matches AGID's own README grammar, confirmed against the real infl.txt
# (see the ledger's Task 2 Step 7 ruling): a variant level is SPACE-separated from the word and
# may be a decimal (`dreamt 1`, `gotten 0.1`); `!` marks a form as likely an inflection of a
# *different*, similar word and is dropped, not kept.
INFL = [
    'find V: found | finding | finds',
    'found V: founded | founding | founds',
    'go V: went | gone | going | goes',
    'lie V: lay | lain | lying | lies',
    'lay V: laid | laying | lays',
    'cat N: cats',
    'dream V: dreamed, dreamt 1 | dreaming | dreams',
    'dive V: dived, dove~ | diving | dives',
    'spell V: spelled, spelt 2 | spelling | spells',
    'wake V: woke, waked? | woken | waking | wakes',
    'learn V: learned, learnt< {chiefly British} | learning | learns',
    'burn V: burned, burnt! | burning | burns',
    'get V: got | got, gotten 0.1 | getting | gets',
    'good A: better | best',
    'well A: better | best',
    'ill A: worse | worst',
    'bad A: worse | worst',
    'zyzzyva N: zyzzyvas',
    'Paris N: Parises',
]


def temp_dir(test: unittest.TestCase) -> Path:
    directory = tempfile.TemporaryDirectory()
    test.addCleanup(directory.cleanup)
    return Path(directory.name)


class ParseFormTests(unittest.TestCase):
    def test_a_plain_form_is_kept(self):
        self.assertEqual(bl.parse_form(' went '), 'went')

    def test_low_confidence_unlisted_and_wrong_lemma_markers_drop_a_form(self):
        for token in ['dove~', 'waked?', 'burnt!']:
            with self.subTest(token=token):
                self.assertIsNone(bl.parse_form(token))

    def test_a_variant_level_of_two_or_more_drops_a_form_even_as_a_decimal(self):
        for token in ['spelt 2', 'spelt 2.1']:
            with self.subTest(token=token):
                self.assertIsNone(bl.parse_form(token))

    def test_kept_markers_decimal_levels_and_annotations_are_stripped(self):
        cases = {
            'learnt<': 'learnt',
            'dreamt 1': 'dreamt',
            'dreamt 0': 'dreamt',
            'gotten 0.1': 'gotten',
            'woke 1.1': 'woke',
            'learnt< {chiefly British}': 'learnt',
            'boxes 0.1 {shrub}': 'boxes',
        }
        for token, form in cases.items():
            with self.subTest(token=token):
                self.assertEqual(bl.parse_form(token), form)

    def test_anything_but_lowercase_ascii_letters_is_not_a_form(self):
        for token in ["o'clock", 'Parises', 'caf\xe9', 'ice-cream', '']:
            with self.subTest(token=token):
                self.assertIsNone(bl.parse_form(token))


class ParseInflLineTests(unittest.TestCase):
    def test_groups_and_variants_are_all_forms_of_the_lemma(self):
        self.assertEqual(
            bl.parse_infl_line('dream V: dreamed, dreamt 1 | dreaming | dreams'),
            ('dream', ['dreamed', 'dreamt', 'dreaming', 'dreams']))

    def test_an_uncertain_part_of_speech_is_read_and_the_lemma_itself_is_not_a_form(self):
        self.assertEqual(
            bl.parse_infl_line('bust V?: busted, bust | busting | busts'),
            ('bust', ['busted', 'busting', 'busts']))

    def test_a_line_that_is_not_an_entry_is_skipped(self):
        for line in ['no colon here', 'a priori A: aprioris', 'Paris N: Parises']:
            with self.subTest(line=line):
                self.assertIsNone(bl.parse_infl_line(line))


class BuildEntriesTests(unittest.TestCase):
    def setUp(self):
        self.entries = bl.build_entries(LEMMA_SIZES, INFL)

    def test_an_inflection_wins_and_a_form_that_is_also_a_lemma_comes_last(self):
        self.assertEqual(self.entries['found'], ['find', 'found'])
        self.assertEqual(self.entries['lay'], ['lie', 'lay'])

    def test_an_inflected_form_in_the_word_list_does_not_list_itself(self):
        self.assertEqual(self.entries['went'], ['go'])
        self.assertEqual(self.entries['cats'], ['cat'])

    def test_a_lemma_maps_to_itself(self):
        self.assertEqual(self.entries['find'], ['find'])
        self.assertEqual(self.entries['the'], ['the'])

    def test_lemmas_are_ordered_by_scowl_size_then_alphabetically(self):
        self.assertEqual(self.entries['better'], ['well', 'good'])
        self.assertEqual(self.entries['worse'], ['bad', 'ill'])

    def test_dropped_markers_leave_no_entry(self):
        for form in ['dove', 'spelt', 'waked', 'burnt']:
            with self.subTest(form=form):
                self.assertNotIn(form, self.entries)

    def test_kept_markers_give_an_entry(self):
        self.assertEqual(self.entries['dreamt'], ['dream'])
        self.assertEqual(self.entries['learnt'], ['learn'])
        self.assertEqual(self.entries['gotten'], ['get'])
        self.assertEqual(self.entries['got'], ['get'])

    def test_lines_of_lemmas_outside_the_word_list_are_ignored(self):
        for form in ['zyzzyva', 'zyzzyvas', 'parises', 'Parises']:
            with self.subTest(form=form):
                self.assertNotIn(form, self.entries)

    def test_every_lemma_has_a_line_of_its_own(self):
        bl.check_closed(self.entries)


class OverrideTests(unittest.TestCase):
    def test_an_override_replaces_the_computed_line(self):
        entries = bl.build_entries(LEMMA_SIZES, INFL)
        overrides = bl.read_overrides(['# comment', '', '   ', 'lay lay lie'])

        bl.apply_overrides(entries, overrides)

        self.assertEqual(overrides, {'lay': ['lay', 'lie']})
        self.assertEqual(entries['lay'], ['lay', 'lie'])

    def test_a_one_word_override_makes_the_form_a_lemma_of_itself(self):
        self.assertEqual(bl.read_overrides(['went']), {'went': ['went']})

    def test_a_malformed_override_is_refused(self):
        for line in ['Lay lay lie', 'lay lay lay', "lay lay l'ie"]:
            with self.subTest(line=line):
                with self.assertRaises(ValueError):
                    bl.read_overrides([line])

    def test_a_repeated_override_form_is_refused(self):
        with self.assertRaises(ValueError):
            bl.read_overrides(['went go', 'went come'])

    def test_a_lemma_with_no_line_of_its_own_is_refused(self):
        with self.assertRaises(ValueError):
            bl.check_closed({'went': ['go']})

    def test_a_lemma_that_does_not_list_itself_is_refused(self):
        # 'lay' claims 'went' is one of its lemmas, but went's own entry says its lemma is 'go' —
        # went is an inflected form, not a lemma, so it may not stand in as one.
        with self.assertRaises(ValueError):
            bl.check_closed({'lay': ['went'], 'went': ['go'], 'go': ['go']})

    def test_an_override_for_a_form_with_no_computed_entry_is_refused(self):
        entries = bl.build_entries(LEMMA_SIZES, INFL)

        with self.assertRaises(ValueError):
            bl.apply_overrides(entries, {'laz': ['laz', 'lie']})


class RenderTests(unittest.TestCase):
    def test_a_lone_lemma_is_one_word_and_anything_else_is_the_form_then_its_lemmas(self):
        text = bl.render({
            'went': ['go'],
            'go': ['go'],
            'found': ['find', 'found'],
            'find': ['find'],
        })

        self.assertEqual(text, 'find\nfound find found\ngo\nwent go\n')

    def test_output_is_sorted_unique_and_ends_in_one_newline(self):
        text = bl.render(bl.build_entries(LEMMA_SIZES, INFL))
        lines = text.split('\n')

        self.assertEqual(lines[-1], '')
        body = lines[:-1]
        self.assertNotIn('', body)
        forms = [line.split(' ')[0] for line in body]
        self.assertEqual(forms, sorted(forms))
        self.assertEqual(len(forms), len(set(forms)))


class ScowlTests(unittest.TestCase):
    def test_lists_up_to_the_size_are_read_keeping_each_words_smallest_size(self):
        final = temp_dir(self)
        (final / 'english-words.10').write_bytes("go\nthe\ncat's\ncaf\xe9\n".encode('latin-1'))
        (final / 'english-words.20').write_bytes(b'go\ndream\nParis\n')
        (final / 'american-words.20').write_bytes(b'color\n')
        (final / 'british-words.20').write_bytes(b'colour\n')
        (final / 'english-words.35').write_bytes(b'dive\n')

        sizes, lists_read = bl.read_scowl_lemmas(final, 20)

        self.assertEqual(sizes, {'go': 10, 'the': 10, 'dream': 20, 'color': 20, 'colour': 20})
        self.assertEqual(lists_read, 4)

    def test_a_missing_english_list_is_refused(self):
        final = temp_dir(self)
        (final / 'american-words.10').write_bytes(b'color\n')

        with self.assertRaises(ValueError):
            bl.read_scowl_lemmas(final, 10)


class FetchTests(unittest.TestCase):
    def setUp(self):
        self.dir = temp_dir(self)
        self.source = self.dir / 'source.bin'
        self.source.write_bytes(b'pinned bytes')
        self.sha = hashlib.sha256(b'pinned bytes').hexdigest()
        self.target = self.dir / 'cache' / 'file.bin'

    def test_a_download_with_the_pinned_checksum_is_kept(self):
        bl.fetch(self.source.as_uri(), self.target, self.sha)

        self.assertEqual(self.target.read_bytes(), b'pinned bytes')

    def test_a_checksum_mismatch_aborts_and_keeps_nothing(self):
        with self.assertRaises(SystemExit):
            bl.fetch(self.source.as_uri(), self.target, '0' * 64)

        self.assertEqual(list((self.dir / 'cache').iterdir()), [])

    def test_a_cached_copy_with_the_pinned_checksum_is_not_fetched_again(self):
        self.target.parent.mkdir(parents=True)
        self.target.write_bytes(b'pinned bytes')

        bl.fetch((self.dir / 'does-not-exist.bin').as_uri(), self.target, self.sha)

        self.assertEqual(self.target.read_bytes(), b'pinned bytes')


class WriteOutputsTests(unittest.TestCase):
    def test_the_data_and_notices_land_identically_in_every_directory(self):
        root = temp_dir(self)
        scowl = root / 'Copyright'
        scowl.write_bytes(b'SCOWL notice\n')
        agid = root / 'README'
        agid.write_bytes(b'AGID notice\n')
        directories = (root / 'domain', root / 'web' / 'public' / 'lexicon')

        bl.write_outputs('go\nwent go\n',
                         {'LICENSE-SCOWL.txt': scowl, 'LICENSE-AGID.txt': agid},
                         directories)

        for directory in directories:
            with self.subTest(directory=str(directory)):
                self.assertEqual((directory / bl.DATA_NAME).read_bytes(), b'go\nwent go\n')
                self.assertEqual((directory / 'LICENSE-SCOWL.txt').read_bytes(), b'SCOWL notice\n')
                self.assertEqual((directory / 'LICENSE-AGID.txt').read_bytes(), b'AGID notice\n')


if __name__ == '__main__':
    unittest.main()
