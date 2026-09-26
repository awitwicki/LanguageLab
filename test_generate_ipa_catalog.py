from generate_ipa_catalog import (
    SECTIONS,
    SYMBOLS,
    audio_filenames_for_language,
    audio_titles,
    best_sound_title,
    commons_title,
    escape_csharp,
    local_sound_filename,
    local_word_filename,
    render_csharp,
    slugify,
    sound_title_candidates,
)


def test_slugify_lowercases_and_joins_with_dashes():
    assert slugify('Voiceless bilabial plosive') == 'voiceless-bilabial-plosive'
    assert slugify('close-mid back rounded vowel') == 'close-mid-back-rounded-vowel'


def test_slugify_strips_characters_a_filename_should_not_carry():
    assert slugify('simultaneous ʃ and x') == 'simultaneous-and-x'
    assert slugify("Wíth àccents") == 'with-accents'
    assert slugify('  spaced  out  ') == 'spaced-out'


def test_sound_title_candidates_start_with_the_name_as_commons_writes_it():
    candidates = sound_title_candidates('voiceless palatal fricative')

    assert candidates[0] == 'File:Voiceless palatal fricative.ogg'


def test_sound_title_candidates_drop_a_leading_voiced_for_sounds_commons_does_not_label():
    # Commons has "Bilabial trill.ogg", not "Voiced bilabial trill.ogg" — trills,
    # nasals and approximants are voiced by default, so the label is often left off.
    candidates = sound_title_candidates('voiced bilabial trill')

    assert 'File:Bilabial trill.ogg' in candidates
    assert candidates.index('File:Voiced bilabial trill.ogg') < candidates.index('File:Bilabial trill.ogg')


def test_sound_title_candidates_offer_every_audio_extension():
    candidates = sound_title_candidates('voiced velar fricative')

    assert 'File:Voiced velar fricative.oga' in candidates
    assert 'File:Voiced velar fricative.wav' in candidates


def test_sound_title_candidates_are_unique_and_keep_their_order():
    candidates = sound_title_candidates('alveolar trill')

    assert len(candidates) == len(set(candidates))
    assert candidates[0] == 'File:Alveolar trill.ogg'


def test_local_sound_filename_is_prefixed_and_keeps_the_source_extension():
    assert local_sound_filename('voiced velar fricative', '.ogg') == 'ipa-voiced-velar-fricative.ogg'
    assert local_sound_filename('bilabial trill', '.wav') == 'ipa-bilabial-trill.wav'


def test_local_word_filename_carries_the_language_so_two_languages_can_share_a_word():
    assert local_word_filename('rue', 'fr', '.ogg', 'close-front-rounded-vowel') == 'rue-fr.ogg'
    assert local_word_filename('rue', 'en', '.ogg', 'close-front-rounded-vowel') == 'rue-en.ogg'


def test_local_word_filename_falls_back_to_the_sound_slug_for_a_non_latin_word():
    # A Cyrillic or Han word slugifies to nothing usable, so the name comes from the
    # sound instead — still a filename, still carrying the language.
    assert local_word_filename('さん', 'ja', '.ogg', 'uvular-nasal') == 'uvular-nasal-word-ja.ogg'
    assert local_word_filename('хата', 'uk', '.wav', 'voiceless-velar-fricative') == (
        'voiceless-velar-fricative-word-uk.wav'
    )


def test_audio_filenames_for_language_reads_the_audio_template_of_that_language_only():
    wikitext = (
        '===Pronunciation===\n'
        '* {{audio|en|LL-Q1860 (eng)-Persent101-ich.wav}}\n'
        '* {{audio|de|De-ich.ogg}}\n'
        '* {{audio|de|De-ich2.ogg}}\n'
    )

    assert audio_filenames_for_language(wikitext, 'de') == ['De-ich.ogg', 'De-ich2.ogg']
    assert audio_filenames_for_language(wikitext, 'en') == ['LL-Q1860 (eng)-Persent101-ich.wav']
    assert audio_filenames_for_language(wikitext, 'fr') == []


def test_audio_filenames_for_language_ignores_the_templates_trailing_parameters():
    wikitext = '* {{audio|fr|Fr-rue.ogg|audio=yes}}\n'

    assert audio_filenames_for_language(wikitext, 'fr') == ['Fr-rue.ogg']


def test_audio_filenames_for_language_handles_a_page_with_no_recordings():
    assert audio_filenames_for_language('===Pronunciation===\n* {{IPA|es|/ˈpero/}}\n', 'es') == []


def test_commons_title_capitalizes_the_first_letter_as_mediawiki_does():
    # Wiktionary writes {{audio|en|en-us-tour.ogg}}, but the file's title on Commons is
    # "File:En-us-tour.ogg" — matching the raw filename finds nothing at all.
    assert commons_title('en-us-tour.ogg') == 'File:En-us-tour.ogg'
    assert commons_title('De-ich.ogg') == 'File:De-ich.ogg'


def test_commons_title_turns_underscores_into_the_spaces_commons_answers_with():
    assert commons_title('LL-Q1860_(eng)-Vealhurl-rue.wav') == 'File:LL-Q1860 (eng)-Vealhurl-rue.wav'


def test_commons_title_leaves_an_already_qualified_title_alone():
    assert commons_title('File:Epiglottal stop.ogg') == 'File:Epiglottal stop.ogg'
    assert commons_title('File:epiglottal stop.ogg') == 'File:Epiglottal stop.ogg'


def test_audio_titles_keeps_recordings_and_drops_charts_and_photos():
    images = [
        'File:Glottal stop.ogg',
        'File:IPA chart 2020.svg',
        'File:Tongue position.png',
        'File:Slow-mo of the bilabial trill.wav',
        'File:Ady-атакъэ.oga',
    ]

    assert audio_titles(images) == [
        'File:Glottal stop.ogg',
        'File:Slow-mo of the bilabial trill.wav',
        'File:Ady-атакъэ.oga',
    ]


def test_best_sound_title_takes_the_file_named_after_the_sound():
    # A sound's Wikipedia article lists every example recording in every language it
    # cites; only the one named after the sound itself is the sound on its own.
    titles = ['File:Cs-naopak.ogg', 'File:En-us-button.ogg', 'File:Glottal stop.ogg']

    assert best_sound_title('Glottal stop', titles) == 'File:Glottal stop.ogg'


def test_best_sound_title_prefers_the_plain_recording_over_an_embellished_one():
    titles = ['File:Slow-mo of the bilabial trill.wav', 'File:Bilabial trill.ogg']

    assert best_sound_title('Bilabial trill', titles) == 'File:Bilabial trill.ogg'


def test_best_sound_title_prefers_ogg_when_two_files_match_equally_well():
    titles = ['File:Bilabial trill.wav', 'File:Bilabial trill.ogg']

    assert best_sound_title('Bilabial trill', titles) == 'File:Bilabial trill.ogg'


def test_best_sound_title_accepts_a_file_that_spells_the_sound_with_extra_words():
    titles = ['File:En-us-inlandnorth-gut.ogg', 'File:PR-open-mid back unrounded vowel2.ogg']

    assert best_sound_title('Open-mid back unrounded vowel', titles) == (
        'File:PR-open-mid back unrounded vowel2.ogg'
    )


def test_best_sound_title_returns_nothing_when_only_example_words_are_listed():
    # Better a silent row than a Czech word standing in for the sound it illustrates.
    titles = ['File:Cs-je.ogg', 'File:Es-Dios.ogg', 'File:Es-ayer.ogg']

    assert best_sound_title('Palatal approximant', titles) is None


def test_best_sound_title_ignores_a_single_shared_word():
    titles = ['File:Vowel chart.ogg', 'File:Es-ayer.ogg']

    assert best_sound_title('Close front rounded vowel', titles) is None


def test_escape_csharp_handles_quotes_and_backslashes():
    assert escape_csharp('a "b" c') == 'a \\"b\\" c'
    assert escape_csharp('a\\b') == 'a\\\\b'


def test_every_symbol_belongs_to_a_declared_section():
    section_keys = {section['key'] for section in SECTIONS}

    for entry in SYMBOLS:
        assert entry['section'] in section_keys, entry['symbol']


def test_every_symbol_carries_a_name_a_hint_and_a_group():
    for entry in SYMBOLS:
        assert entry['symbol']
        assert entry['name'], entry['symbol']
        assert entry['hint'], entry['symbol']
        assert entry['group'], entry['symbol']


def test_no_symbol_is_listed_twice():
    symbols = [entry['symbol'] for entry in SYMBOLS]

    assert len(symbols) == len(set(symbols))


def test_an_example_word_always_comes_with_its_language_and_transcription():
    for entry in SYMBOLS:
        has_word = bool(entry.get('word'))
        assert has_word == bool(entry.get('language')), entry['symbol']
        assert has_word == bool(entry.get('ipa')), entry['symbol']
        if has_word:
            assert entry.get('lang_code'), entry['symbol']


def test_english_examples_are_marked_as_english_sounds():
    # The "only English sounds" filter reads `english`; an entry whose example is an
    # English word but that is not marked would be filtered out of its own section.
    for entry in SYMBOLS:
        if entry.get('language') == 'English':
            assert entry.get('english'), entry['symbol']


def test_the_english_inventory_covers_the_consonants_and_vowels_english_uses():
    english = {entry['symbol'] for entry in SYMBOLS if entry.get('english')}

    for symbol in 'pbtdkɡfvθðszʃʒmnŋlɹjwh':
        assert symbol in english, symbol
    for symbol in ['ɪ', 'i', 'ʊ', 'u', 'ɛ', 'æ', 'ʌ', 'ɔ', 'ɑ', 'ə', 'eɪ', 'aɪ', 'ɔɪ', 'aʊ', 'oʊ']:
        assert symbol in english, symbol


def test_render_csharp_nests_every_entry_under_its_section():
    sections = [
        {
            'key': 'vowels',
            'title': 'Vowels',
            'note': 'Shaped by the tongue.',
            'entries': [
                {
                    'symbol': 'i',
                    'name': 'close front unrounded vowel',
                    'hint': 'ee as in "sheep"',
                    'group': 'Close',
                    'english': True,
                    'word': 'sheep',
                    'language': 'English',
                    'ipa': '/ʃiːp/',
                    'sound_audio_file': 'ipa-close-front-unrounded-vowel.ogg',
                    'word_audio_file': None,
                    'aliases': ['iː'],
                }
            ],
        }
    ]

    rendered = render_csharp(sections)

    assert 'public static partial class IpaCatalog' in rendered
    assert 'new("vowels", "Vowels", "Shaped by the tongue."' in rendered
    assert (
        'new("i", "close front unrounded vowel", "ee as in \\"sheep\\"", "Close", true, '
        '"sheep", "English", "/ʃiːp/", "ipa-close-front-unrounded-vowel.ogg", null, '
        'new[] { "iː" }),' in rendered
    )


def test_render_csharp_writes_null_for_a_symbol_with_no_example_word():
    sections = [
        {
            'key': 'other',
            'title': 'Other symbols',
            'note': '',
            'entries': [
                {
                    'symbol': 'ʜ',
                    'name': 'voiceless epiglottal fricative',
                    'hint': 'a rasping h deep in the throat',
                    'group': 'Other',
                    'english': False,
                    'word': None,
                    'language': None,
                    'ipa': None,
                    'sound_audio_file': None,
                    'word_audio_file': None,
                    'aliases': [],
                }
            ],
        }
    ]

    rendered = render_csharp(sections)

    assert 'new("ʜ", "voiceless epiglottal fricative", "a rasping h deep in the throat", "Other", false, null, null, null, null, null, new string[0]),' in rendered
