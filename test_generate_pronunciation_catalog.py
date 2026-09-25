from generate_pronunciation_catalog import (
    covered_phonemes,
    is_usable_candidate,
    missing_phonemes,
    select_words_for_family,
    strip_stress,
)

TH_FAMILY = {
    'key': 'th-sounds',
    'title': 'TH sounds',
    'target_sounds': ['θ', 'ð'],
    'phonemes': ['TH', 'DH'],
}

FINAL_DEVOICING_FAMILY = {
    'key': 'final-devoicing',
    'title': 'Voiced endings',
    'target_sounds': ['b', 'd'],
    'phonemes': ['B', 'D'],
    'final_only': True,
}

SCHWA_FAMILY = {
    'key': 'schwa',
    'title': 'The schwa',
    'target_sounds': ['ə'],
    'phonemes': ['AH0'],
    'stress_sensitive': True,
}


def test_strip_stress_removes_trailing_digit():
    assert strip_stress('AH0') == 'AH'
    assert strip_stress('IH1') == 'IH'
    assert strip_stress('NG') == 'NG'


def test_covered_phonemes_finds_target_sounds_anywhere_by_default():
    pronunciation = ['DH', 'IH1', 'S']
    assert covered_phonemes(pronunciation, TH_FAMILY) == {'DH'}


def test_covered_phonemes_ignores_stress_digits_unless_stress_sensitive():
    pronunciation = ['TH', 'IH1', 'NG', 'K']
    assert covered_phonemes(pronunciation, TH_FAMILY) == {'TH'}


def test_covered_phonemes_final_only_requires_last_position():
    ends_in_target = ['K', 'AE1', 'B']
    starts_with_target = ['B', 'AE1', 'K']
    assert covered_phonemes(ends_in_target, FINAL_DEVOICING_FAMILY) == {'B'}
    assert covered_phonemes(starts_with_target, FINAL_DEVOICING_FAMILY) == set()


def test_covered_phonemes_stress_sensitive_keeps_the_digit():
    unstressed = ['AH0', 'B', 'AH1', 'V']
    assert covered_phonemes(unstressed, SCHWA_FAMILY) == {'AH0'}
    stressed_only = ['AH1', 'B', 'AH2', 'V']
    assert covered_phonemes(stressed_only, SCHWA_FAMILY) == set()


def test_select_words_for_family_prefers_widest_coverage_first():
    pronouncing_dict = {
        'thin': [['TH', 'IH1', 'N']],
        'this': [['DH', 'IH1', 'S']],
        'width': [['W', 'IH1', 'D', 'TH']],
        'other': [['AH1', 'DH', 'ER0']],
    }
    frequency = {'thin': 5, 'this': 100, 'width': 1, 'other': 50}

    selected = select_words_for_family(TH_FAMILY, pronouncing_dict, frequency, max_words=12)

    assert set(selected) <= set(pronouncing_dict)
    covered = set()
    for word in selected:
        covered |= covered_phonemes(pronouncing_dict[word][0], TH_FAMILY)
    assert covered == {'TH', 'DH'}


def test_select_words_for_family_stops_once_covered():
    pronouncing_dict = {
        'thin': [['TH', 'IH1', 'N']],
        'mother': [['M', 'AH1', 'DH', 'ER0']],
        'unrelated': [['K', 'AE1', 'T']],
    }
    frequency = {'thin': 1, 'mother': 1, 'unrelated': 100}

    selected = select_words_for_family(TH_FAMILY, pronouncing_dict, frequency, max_words=12)

    assert 'unrelated' not in selected
    assert len(selected) == 2


def test_select_words_for_family_respects_max_words():
    # Five target phonemes, one distinct phoneme covered per word — full coverage would
    # take 5 picks, so a max_words=3 cap is the only thing that can stop it at 3.
    wide_family = {
        'key': 'wide',
        'title': 'Five sounds',
        'target_sounds': ['a', 'b', 'c', 'd', 'e'],
        'phonemes': ['AA', 'B', 'CH', 'D', 'EH'],
    }
    pronouncing_dict = {
        'wordaa': [['AA', 'T']],
        'wordb': [['B', 'T']],
        'wordch': [['CH', 'T']],
        'wordd': [['D', 'T']],
        'wordeh': [['EH', 'T']],
    }
    frequency = {word: 1 for word in pronouncing_dict}

    selected = select_words_for_family(wide_family, pronouncing_dict, frequency, max_words=3)

    assert len(selected) == 3


def test_select_words_for_family_pads_with_redundant_candidates_beyond_minimal_cover():
    # A single-phoneme family: any one candidate already achieves full coverage, so the
    # minimal cover is just 1 word — but a network-facing caller needs fallback options
    # in case that word's audio turns out to be unavailable, so the pool should keep
    # ranking the rest of the covering candidates by frequency instead of stopping.
    single_phoneme_family = {
        'key': 'single',
        'title': 'One sound',
        'target_sounds': ['r'],
        'phonemes': ['R'],
    }
    pronouncing_dict = {
        'rare': [['R', 'EH1', 'R']],
        'red': [['R', 'EH1', 'D']],
        'run': [['R', 'AH1', 'N']],
        'rat': [['R', 'AE1', 'T']],
        'nope': [['N', 'OW1', 'P']],
    }
    frequency = {'rare': 1, 'red': 100, 'run': 50, 'rat': 10, 'nope': 1000}

    selected = select_words_for_family(single_phoneme_family, pronouncing_dict, frequency, max_words=12)

    assert selected[0] == 'red'  # the minimal cover still picks the most frequent covering word first
    assert len(selected) == 4  # every R-covering candidate is kept as a fallback, not just the 1 needed
    assert 'nope' not in selected  # words that don't cover the target sound are never included


def test_is_usable_candidate_rejects_function_words_and_non_words():
    assert is_usable_candidate('ship')
    assert is_usable_candidate('mother')
    # Weak-form-prone grammatical words: what the learner records is not what
    # Wiktionary transcribes for them.
    assert not is_usable_candidate('the')
    assert not is_usable_candidate('for')
    assert not is_usable_candidate('a')
    assert not is_usable_candidate('has')
    # Not usable as a Wiktionary page title or an audio file name.
    assert not is_usable_candidate("'em")
    assert not is_usable_candidate('a.d.')


def test_select_words_for_family_never_picks_a_function_word():
    # 'the' and 'that' are the two most frequent DH words in English by a wide margin,
    # so frequency ranking alone put them in the catalog; the stoplist has to beat that.
    pronouncing_dict = {
        'the': [['DH', 'AH0']],
        'that': [['DH', 'AE1', 'T']],
        'mother': [['M', 'AH1', 'DH', 'ER0']],
        'think': [['TH', 'IH1', 'NG', 'K']],
    }
    frequency = {'the': 70000, 'that': 10000, 'mother': 100, 'think': 200}

    selected = select_words_for_family(TH_FAMILY, pronouncing_dict, frequency, max_words=12)

    assert 'the' not in selected
    assert 'that' not in selected
    assert set(selected) == {'mother', 'think'}


def test_missing_phonemes_reports_what_the_final_word_list_does_not_cover():
    pronouncing_dict = {'think': [['TH', 'IH1', 'NG', 'K']], 'mother': [['M', 'AH1', 'DH', 'ER0']]}

    only_th = missing_phonemes(TH_FAMILY, [{'word': 'think'}], pronouncing_dict)
    both = missing_phonemes(TH_FAMILY, [{'word': 'think'}, {'word': 'mother'}], pronouncing_dict)
    nothing_survived = missing_phonemes(TH_FAMILY, [], pronouncing_dict)

    assert only_th == ['DH']  # the word that would have covered DH lost its audio
    assert both == []
    assert nothing_survived == ['DH', 'TH']


from generate_pronunciation_catalog import escape_csharp, parse_pronunciation_data, render_csharp


def test_parse_pronunciation_data_pairs_one_shared_ipa_with_both_accent_recordings():
    # Both accents say this word the same way, so Wiktionary lists a single IPA line
    # with both recordings under it — each accent is paired with that same transcription.
    wikitext = (
        '===Pronunciation===\n'
        '* {{IPA|en|/ˈʃɪp/|[ˈʃʰɪp]}}\n'
        '** {{audio|en|en-uk-a ship.ogg|a=UK|text=a ship}}\n'
        '** {{audio|en|en-us-ship.ogg|a=US}}\n'
    )

    result = parse_pronunciation_data(wikitext)

    assert result == {
        'uk': {'ipa': '/ˈʃɪp/', 'audio_filename': 'en-uk-a ship.ogg'},
        'us': {'ipa': '/ˈʃɪp/', 'audio_filename': 'en-us-ship.ogg'},
    }


def test_parse_pronunciation_data_keeps_each_accents_own_ipa_with_its_own_audio():
    # The real shape of en.wiktionary.org/wiki/for, and the exact bug this fixes: the
    # RP transcription has no r in it at all, so pairing it with the US recording made
    # the R-sound family display a word that visibly doesn't demonstrate an R.
    wikitext = (
        '===Pronunciation===\n'
        '** {{IPA|en|/fɔː/|a=RP}}\n'
        '*** {{audio|en|LL-Q1860 (eng)-Pvanp7-for.wav|a=UK,stressed}}\n'
        '** {{IPA|en|/foɹ/|a=GA,CA}}\n'
        '*** {{audio|en|En-us-for.ogg|a=GA,stressed}}\n'
    )

    result = parse_pronunciation_data(wikitext)

    assert result['us']['ipa'] == '/foɹ/'
    assert result['us']['audio_filename'] == 'En-us-for.ogg'
    assert result['uk']['ipa'] == '/fɔː/'
    assert result['uk']['audio_filename'] == 'LL-Q1860 (eng)-Pvanp7-for.wav'


def test_parse_pronunciation_data_does_not_pair_across_a_section_boundary():
    # A second etymology is a different pronunciation; the first section's IPA must not
    # reach forward and claim it.
    wikitext = (
        '===Pronunciation===\n'
        '* {{IPA|en|/liːd/}}\n'
        '** {{audio|en|en-us-lead-verb.ogg|a=US}}\n'
        '\n'
        '===Etymology 2===\n'
        '====Pronunciation====\n'
        '* {{audio|en|en-uk-lead-metal.ogg|a=UK}}\n'
    )

    result = parse_pronunciation_data(wikitext)

    assert result == {'us': {'ipa': '/liːd/', 'audio_filename': 'en-us-lead-verb.ogg'}}


def test_parse_pronunciation_data_prefers_the_phonemic_transcription_over_the_narrow_one():
    # The real shape of en.wiktionary.org/wiki/man: the US recording is filed under a
    # narrow æ-tensing transcription that contains no æ at all, which is the sound the
    # family teaches. The section's general GA transcription is the one to show.
    wikitext = (
        '===Pronunciation===\n'
        '* {{IPA|en|/ˈmæn/|a=RP,GA}}\n'
        '* {{IPA|en|[ˈmɛə̯n]|[ˈmeə̯n]|a=US,Canada,æ-tensing}}\n'
        '** {{audio|en|en-us-man.ogg|a=US,æ-tensing}}\n'
        '* {{IPA|en|/ˈman/|/ˈmaːn/|a=SSB|a2=bad-lad split}}\n'
        '** {{audio|en|en-uk-man.ogg|a=SSB,bad-lad split}}\n'
    )

    result = parse_pronunciation_data(wikitext)

    assert result['us'] == {'ipa': '/ˈmæn/', 'audio_filename': 'en-us-man.ogg'}
    assert result['uk'] == {'ipa': '/ˈman/', 'audio_filename': 'en-uk-man.ogg'}


def test_parse_pronunciation_data_takes_the_phonemic_value_from_the_owning_template_first():
    # When the owning template has both, its own phonemic value wins — no lookback.
    wikitext = '* {{IPA|en|/ˈθɪŋk/|[ˈθɪŋk]|a=RP,GA,CA}}\n** {{audio|en|en-us-think.ogg|a=US}}\n'

    assert parse_pronunciation_data(wikitext)['us']['ipa'] == '/ˈθɪŋk/'


def test_parse_pronunciation_data_does_not_borrow_another_accents_transcription():
    # The only phonemic line is explicitly RP, so it must not be shown for the US
    # recording; the narrow US line it is actually filed under is used instead.
    wikitext = (
        '* {{IPA|en|/ˈbɑːθ/|a=RP}}\n'
        '* {{IPA|en|[bæθ]|a=Northern England}}\n'
        '** {{audio|en|en-us-bath.ogg|a=US}}\n'
    )

    assert parse_pronunciation_data(wikitext)['us']['ipa'] == '[bæθ]'


def test_parse_pronunciation_data_pairs_audio_listed_at_the_same_bullet_level():
    # en.wiktionary.org/wiki/which files both recordings as siblings of the IPA lines
    # rather than as sub-bullets; the recording still belongs to the IPA it follows.
    wikitext = (
        '* {{enPR|wĭch}}, {{IPA|en|/wɪt͡ʃ/}}\n'
        '* {{enPR|hwĭch|a=non-wine-whine}}, {{IPA|en|/ʍɪt͡ʃ/}}\n'
        '* {{audio|en|en-us-which.ogg|a=US,wine-whine}}\n'
    )

    assert parse_pronunciation_data(wikitext)['us']['ipa'] == '/ʍɪt͡ʃ/'


def test_parse_pronunciation_data_handles_a_page_with_no_pronunciation_section():
    assert parse_pronunciation_data('===Noun===\nA word with no pronunciation data at all.') == {}


def test_parse_pronunciation_data_ignores_audio_without_a_recognized_accent():
    wikitext = '{{IPA|en|/wɜːd/}}\n{{audio|en|some-other-language.ogg}}'

    assert parse_pronunciation_data(wikitext) == {}


def test_parse_pronunciation_data_drops_an_accent_whose_audio_is_missing():
    # Audio for one accent only: the other accent has no (ipa, audio) pair, so the
    # caller's "both accents required" filter drops the word instead of showing the
    # UK recording under a US heading.
    wikitext = '* {{IPA|en|/kæt/}}\n** {{audio|en|en-us-cat.ogg|a=US}}\n'

    result = parse_pronunciation_data(wikitext)

    assert set(result) == {'us'}


def test_escape_csharp_handles_quotes_and_backslashes():
    assert escape_csharp('say "hi"') == 'say \\"hi\\"'
    assert escape_csharp('back\\slash') == 'back\\\\slash'


def test_render_csharp_produces_families_and_words_lists():
    families = [
        {
            'key': 'th-sounds',
            'title': 'TH sounds',
            'target_sounds': ['θ', 'ð'],
            'words': [
                {'word': 'this', 'ipa': '/ðɪs/', 'audio_us_file': 'this-us.ogg', 'audio_uk_file': 'this-uk.ogg'},
            ],
        },
    ]

    output = render_csharp(families)

    assert 'namespace LanguageLab.Domain.Pronunciation;' in output
    assert 'public static partial class PronunciationCatalog' in output
    assert 'new("th-sounds", "TH sounds", new[] { "θ", "ð" })' in output
    assert 'new("this", "/ðɪs/", "th-sounds", "this-us.ogg", "this-uk.ogg")' in output
