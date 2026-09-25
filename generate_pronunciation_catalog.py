"""Builds the pronunciation-trainer word catalog: picks words that give the
widest coverage of target English sounds per family, looks up real US/UK
recordings, and emits LanguageLab.Domain/Pronunciation/PronunciationCatalog.Generated.cs.

Run with: uv run generate_pronunciation_catalog.py
Requires network access (en.wiktionary.org, commons.wikimedia.org) and the NLTK cmudict/brown corpora
(downloaded automatically on first run).
"""

from pathlib import Path

AUDIO_DIR = Path('web/public/pronunciation-audio')
CATALOG_OUTPUT = Path('LanguageLab.Domain/Pronunciation/PronunciationCatalog.Generated.cs')

# Target sounds picked for contrasts that don't exist in Ukrainian, or are
# commonly confused by Ukrainian speakers of English. `phonemes` are CMU
# Pronouncing Dictionary (ARPAbet) codes; `target_sounds` are the IPA symbols
# shown to the learner. `final_only` restricts matches to the word's last
# phoneme (final-consonant devoicing only matters word-finally);
# `stress_sensitive` keeps the stress digit in the match (the schwa AH0 is a
# different sound from stressed AH1/AH2, not just an unstressed spelling of
# the same one).
FAMILIES = [
    {'key': 'th-sounds', 'title': 'TH sounds', 'target_sounds': ['θ', 'ð'], 'phonemes': ['TH', 'DH']},
    {'key': 'r-sound', 'title': 'The English R', 'target_sounds': ['r'], 'phonemes': ['R']},
    {'key': 'w-vs-v', 'title': 'W vs V', 'target_sounds': ['w', 'v'], 'phonemes': ['W', 'V']},
    {'key': 'ae-vs-eh', 'title': 'Æ vs Ɛ (cat vs bed)', 'target_sounds': ['æ', 'ɛ'], 'phonemes': ['AE', 'EH']},
    {'key': 'ih-vs-iy', 'title': 'Ɪ vs Iː (ship vs sheep)', 'target_sounds': ['ɪ', 'iː'], 'phonemes': ['IH', 'IY']},
    {
        'key': 'final-devoicing',
        'title': 'Voiced endings',
        'target_sounds': ['b', 'd', 'ɡ', 'v', 'z'],
        'phonemes': ['B', 'D', 'G', 'V', 'Z'],
        'final_only': True,
    },
    {'key': 'ng-sound', 'title': 'The NG sound', 'target_sounds': ['ŋ'], 'phonemes': ['NG']},
    {'key': 'schwa', 'title': 'The schwa', 'target_sounds': ['ə'], 'phonemes': ['AH0'], 'stress_sensitive': True},
    {'key': 'h-vs-g', 'title': 'H vs G', 'target_sounds': ['h', 'ɡ'], 'phonemes': ['HH', 'G']},
]

# How many candidates a family may try. It doesn't bound the family's size — a word is
# only kept when it covers a target sound nothing else has covered yet — it bounds how
# many fallbacks there are when a candidate turns out to have no usable audio, which is
# the common case. Twelve left the voiced-endings family without a word for /z/.
MAX_WORDS_PER_FAMILY = 24

# Grammatical function words are excluded from candidacy entirely. In connected
# speech they are normally said as a reduced weak form ("for" as /fə/, "the" as
# /ðə/, "a" as /ə/) that differs from the citation form a learner would record
# into the microphone — and Wiktionary transcribes several of them in that weak
# form, so they teach a target the exercise never asks for. Content words don't
# have this citation-vs-connected-speech split, so restricting the pool to them
# also stops raw Brown-corpus frequency (which these words dominate) from
# filling every family with them.
FUNCTION_WORD_STOPLIST = {
    'a', 'am', 'an', 'and', 'are', 'as', 'at', 'be', 'been', 'being', 'but', 'by',
    'can', 'could', 'did', 'do', 'does', 'for', 'from', 'had', 'has', 'have', 'he',
    'her', 'him', 'his', 'i', 'if', 'in', 'is', 'it', 'its', 'may', 'me', 'might',
    'must', 'my', 'no', 'nor', 'not', 'of', 'on', 'or', 'our', 'shall', 'she',
    'should', 'so', 'than', 'that', 'the', 'their', 'them', 'there', 'these',
    'they', 'this', 'those', 'to', 'us', 'was', 'we', 'were', 'what', 'which',
    'who', 'whom', 'whose', 'will', 'with', 'would', 'you', 'your',
}


def is_usable_candidate(word: str) -> bool:
    """A catalog word has to be a plain single word: it doubles as a Wiktionary
    page title and as an audio file name, and it is what the learner is asked to
    say. The CMU dictionary also contains abbreviations ("a.d."), contractions
    ("'em") and hyphenated entries, none of which work as either — and function
    words are excluded for the reason FUNCTION_WORD_STOPLIST documents.
    """
    return word.isalpha() and word not in FUNCTION_WORD_STOPLIST


def strip_stress(phoneme: str) -> str:
    return phoneme[:-1] if phoneme[-1].isdigit() else phoneme


def covered_phonemes(pronunciation: list[str], family: dict) -> set[str]:
    targets = set(family['phonemes'])
    covered = set()
    last_index = len(pronunciation) - 1

    for i, raw in enumerate(pronunciation):
        if family.get('final_only') and i != last_index:
            continue
        candidate = raw if family.get('stress_sensitive') else strip_stress(raw)
        if candidate in targets:
            covered.add(candidate)

    return covered


def select_words_for_family(
    family: dict,
    pronouncing_dict: dict[str, list[list[str]]],
    frequency: dict[str, int],
    max_words: int = MAX_WORDS_PER_FAMILY,
) -> list[str]:
    candidates: dict[str, set[str]] = {}
    for word, prons in pronouncing_dict.items():
        if not is_usable_candidate(word):
            continue
        covered = covered_phonemes(prons[0], family)
        if covered:
            candidates[word] = covered

    remaining = set(family['phonemes'])
    selected: list[str] = []

    while remaining and len(selected) < max_words:
        best_word = None
        best_gain: set[str] = set()

        for word, covered in candidates.items():
            if word in selected:
                continue
            gain = covered & remaining
            if len(gain) > len(best_gain) or (
                len(gain) == len(best_gain)
                and gain
                and frequency.get(word, 0) > frequency.get(best_word, 0)
            ):
                best_word, best_gain = word, gain

        if best_word is None or not best_gain:
            break

        selected.append(best_word)
        remaining -= best_gain

    # Phase 2: redundancy — once coverage is complete (or exhausted), keep ranking
    # more candidates by frequency so a network-facing caller has fallback options
    # when its top pick turns out to have no usable audio for one accent.
    remaining_candidates = sorted(
        (w for w in candidates if w not in selected),
        key=lambda w: frequency.get(w, 0),
        reverse=True,
    )
    for word in remaining_candidates:
        if len(selected) >= max_words:
            break
        selected.append(word)

    return selected


import re
import time

import requests

WIKTIONARY_API = 'https://en.wiktionary.org/w/api.php'
COMMONS_API = 'https://commons.wikimedia.org/w/api.php'

# Wikimedia's API and file-upload servers reject requests with no descriptive
# User-Agent (bot policy: https://meta.wikimedia.org/wiki/User-Agent_policy) —
# requests' default UA gets a 403 from both en.wiktionary.org and
# upload.wikimedia.org, so every request here needs this header explicitly.
HTTP_HEADERS = {
    'User-Agent': 'LanguageLab-PronunciationCatalog/1.0 '
    '(https://github.com/awitwicki/LanguageLab; witwicki666@gmail.com)'
}

IPA_TEMPLATE_RE = re.compile(r'\{\{IPA\|en\|([^{}]*)\}\}')
# Accent labels Wiktionary puts on a general transcription for each accent
# (General American, Received Pronunciation, Standard Southern British).
GENERIC_ACCENT_TAGS = {'us': {'us', 'ga', 'genam'}, 'uk': {'uk', 'rp', 'ssb'}}
# Captures the whole {{audio|en|...}} body (filename plus any |key=value params)
# so parse_pronunciation_data can read the template's own a= accent label —
# newer Wiktionary audio uploads (Lingua Libre recordings) use filenames like
# "LL-Q1860 (eng)-Pvanp7-for.wav" that don't carry an en-us-/en-uk- prefix at
# all, so the accent label is the only reliable signal for those.
AUDIO_TEMPLATE_RE = re.compile(r'\{\{audio\|en\|([^{}]*)\}\}')
# A wikitext section heading (==English==, ===Pronunciation===, …). An IPA line's
# audio always sits directly under it as a sub-bullet, so a heading ends the region
# an IPA template can claim audio from — without this bound the last IPA on a page
# would reach into a later etymology's recordings, which are a different pronunciation.
HEADING_RE = re.compile(r'^=+[^=\n]+=+\s*$', re.MULTILINE)


def fetch_wiktionary_wikitext(word: str) -> str:
    response = requests.get(
        WIKTIONARY_API,
        params={'action': 'parse', 'page': word, 'prop': 'wikitext', 'format': 'json'},
        timeout=10,
        headers=HTTP_HEADERS,
    )
    if response.status_code != 200:
        return ''

    data = response.json()
    if 'error' in data:
        return ''

    return data.get('parse', {}).get('wikitext', {}).get('*', '')


def classify_audio_accent(template_body: str) -> tuple[str, str] | None:
    """Reads one {{audio|en|…}} template body into (accent, filename), or None
    when it is neither a US nor a UK recording. The template's own a= label is
    the primary signal — newer Lingua Libre uploads have filenames like
    "LL-Q1860 (eng)-Pvanp7-for.wav" with no accent in them at all — with the
    long-established en-us-/en-uk- filename prefix as the fallback.
    """
    params = template_body.split('|')
    filename = params[0].strip()
    lowered = filename.lower()

    accent_tags: list[str] = []
    for param in params[1:]:
        if param.startswith('a='):
            accent_tags = [tag.strip().lower() for tag in param[2:].split(',')]
            break

    if 'us' in accent_tags or 'ga' in accent_tags or lowered.startswith('en-us-'):
        return 'us', filename
    if 'uk' in accent_tags or 'rp' in accent_tags or lowered.startswith('en-uk-'):
        return 'uk', filename
    return None


def parse_ipa_template(body: str) -> dict:
    """Splits one {{IPA|en|…}} body into its transcriptions and accent labels.

    Positional parameters are transcriptions — phonemic (/ˈmæn/) and often also
    narrow phonetic ([ˈmɛə̯n]) — while a=/a2= carry the accent labels.
    """
    params = body.split('|')
    values = [p.strip() for p in params if '=' not in p and p.strip()]

    tags: list[str] = []
    for param in params:
        key, sep, value = param.partition('=')
        if sep and key.strip() in ('a', 'a2'):
            tags += [tag.strip().lower() for tag in value.split(',')]

    return {'values': values, 'tags': tags}


def phonemic_value(template: dict) -> str:
    """The template's phonemic (slash-delimited) transcription, if it has one.

    A bracketed value is a narrow phonetic transcription of one regional variety —
    "man" as [ˈmɛə̯n] under æ-tensing — which is the wrong thing to show a learner:
    it is harder to read and, in that example, doesn't contain the æ the family is
    teaching at all. The phonemic form is what the rest of the catalog displays.
    """
    return next((value for value in template['values'] if value.startswith('/')), '')


def display_ipa(templates: list[dict], owner_index: int, accent: str) -> str:
    """The transcription to show for `accent`, given the IPA template its recording
    is filed under. Normally that template's own phonemic value; when the recording
    hangs off a narrow-only line, the section's general transcription for this accent
    (tagged GA/US or RP/UK/SSB, or untagged and so accent-neutral) is used instead.
    """
    owner = templates[owner_index]
    own = phonemic_value(owner)
    if own:
        return own

    for candidate in reversed(templates[:owner_index]):
        if candidate['section'] != owner['section']:
            break
        if candidate['tags'] and not (set(candidate['tags']) & GENERIC_ACCENT_TAGS[accent]):
            continue
        general = phonemic_value(candidate)
        if general:
            return general

    return owner['values'][0] if owner['values'] else ''


def parse_pronunciation_data(wikitext: str) -> dict[str, dict[str, str]]:
    """Pairs each accent's IPA with that same accent's recording.

    Returns {'us': {'ipa': …, 'audio_filename': …}, 'uk': {…}}, with an accent
    absent when the page has no (IPA, audio) pair for it.

    Wiktionary lists one IPA line per accent with that accent's audio directly
    beneath it as a sub-bullet:

        ** {{IPA|en|/fɔː/|a=RP}}
        *** {{audio|en|LL-Q1860 (eng)-Pvanp7-for.wav|a=UK,stressed}}
        ** {{IPA|en|/foɹ/|a=GA,CA}}
        *** {{audio|en|En-us-for.ogg|a=GA,stressed}}

    So a recording belongs to the last IPA template before it in the same section.
    Taking "the first IPA on the page" together with "any US audio anywhere on the
    page" instead is what made the catalog show "for" as the non-rhotic /fɔː/ — an
    RP transcription with no r in it — next to the US recording.
    """
    headings = [match.start() for match in HEADING_RE.finditer(wikitext)]

    def section_of(position: int) -> int:
        return sum(1 for heading in headings if heading < position)

    templates = []
    for match in IPA_TEMPLATE_RE.finditer(wikitext):
        template = parse_ipa_template(match.group(1))
        template['end'] = match.end()
        template['section'] = section_of(match.start())
        templates.append(template)

    accents: dict[str, dict[str, str]] = {}
    for audio_match in AUDIO_TEMPLATE_RE.finditer(wikitext):
        classified = classify_audio_accent(audio_match.group(1))
        if not classified:
            continue

        accent, filename = classified
        if accent in accents:
            continue

        section = section_of(audio_match.start())
        owner_index = next(
            (
                index
                for index in range(len(templates) - 1, -1, -1)
                if templates[index]['end'] <= audio_match.start() and templates[index]['section'] == section
            ),
            None,
        )
        if owner_index is None:
            continue

        accents[accent] = {'ipa': display_ipa(templates, owner_index, accent), 'audio_filename': filename}

    return accents


def resolve_commons_url(filename: str) -> str | None:
    response = requests.get(
        COMMONS_API,
        params={'action': 'query', 'titles': f'File:{filename}', 'prop': 'imageinfo', 'iiprop': 'url', 'format': 'json'},
        timeout=10,
        headers=HTTP_HEADERS,
    )
    if response.status_code != 200:
        return None

    pages = response.json().get('query', {}).get('pages', {})
    for page in pages.values():
        imageinfo = page.get('imageinfo')
        if imageinfo:
            return imageinfo[0]['url']

    return None


def download_audio(url: str, path: Path) -> str | None:
    """Downloads to `path`, but with its suffix corrected to match the real
    audio format: most Wiktionary/Commons pronunciation recordings are Ogg
    Vorbis, but newer Lingua Libre uploads are WAV — saving those under a
    `.ogg` name would ship a file whose contents don't match its extension.
    Both formats play natively in the browsers this feature already requires
    (Chrome, Edge), so either is fine as long as it's labeled correctly.
    Returns the file name actually written, or None on failure.
    """
    response = requests.get(url, timeout=10, headers=HTTP_HEADERS)
    if response.status_code != 200 or not response.content:
        return None

    suffix = '.wav' if response.content[:4] == b'RIFF' else '.ogg'
    real_path = path.with_suffix(suffix)
    real_path.write_bytes(response.content)
    return real_path.name


def build_catalog(pronouncing_dict: dict[str, list[list[str]]], frequency: dict[str, int]) -> list[dict]:
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    families_out = []
    used_words: set[str] = set()

    for family in FAMILIES:
        candidates = select_words_for_family(family, pronouncing_dict, frequency)
        words_out = []
        remaining = set(family['phonemes'])

        for word in candidates:
            if not remaining:
                break
            if word in used_words:
                continue

            gain = covered_phonemes(pronouncing_dict[word][0], family) & remaining
            if not gain:
                continue

            wikitext = fetch_wiktionary_wikitext(word)
            # Throttle every candidate that reaches the network, not just the ones that
            # succeed all the way through — a discarded candidate (no wikitext, missing
            # accent audio, failed Commons lookup, failed download) still made this one
            # request, and with the Phase 2 redundancy pool a family can now try up to
            # max_words candidates, most of which are expected to fail partway through.
            # Pausing here also covers every later step for this candidate, since they
            # only run after this call already paid the pause.
            time.sleep(0.3)
            if not wikitext:
                continue

            accents = parse_pronunciation_data(wikitext)
            # Both accents must have a matched (IPA, audio) pair, not merely an audio
            # file found somewhere on the page — an accent whose recording could not be
            # tied to a transcription would be shown under another accent's IPA.
            if 'us' not in accents or 'uk' not in accents:
                continue

            us_url = resolve_commons_url(accents['us']['audio_filename'])
            uk_url = resolve_commons_url(accents['uk']['audio_filename'])
            if not us_url or not uk_url:
                continue

            us_path = AUDIO_DIR / f'{word}-us.ogg'
            uk_path = AUDIO_DIR / f'{word}-uk.ogg'
            us_file = download_audio(us_url, us_path)
            uk_file = download_audio(uk_url, uk_path)
            if not us_file or not uk_file:
                continue

            words_out.append({
                # The app defaults to the US accent, so the word's displayed IPA is the
                # US entry's own transcription — the one paired with the US recording.
                'word': word,
                'ipa': accents['us']['ipa'],
                'audio_us_file': us_file,
                'audio_uk_file': uk_file,
            })
            used_words.add(word)
            remaining -= gain

        missing = missing_phonemes(family, words_out, pronouncing_dict)
        if missing:
            # Only this script has the ARPAbet data to tell whether a family's final word
            # list really demonstrates the sounds it advertises (the C# side sees opaque
            # display strings), so a partial regeneration has to be loud here or it ships
            # a family that doesn't teach what its title claims.
            print(
                f"WARNING: family '{family['key']}' does not cover {missing} — "
                f'no word with audio for both accents was found for those sounds'
            )

        families_out.append({
            'key': family['key'],
            'title': family['title'],
            'target_sounds': family['target_sounds'],
            'words': words_out,
            'missing_phonemes': missing,
        })

    return families_out


def missing_phonemes(
    family: dict,
    words_out: list[dict],
    pronouncing_dict: dict[str, list[list[str]]],
) -> list[str]:
    """Target phonemes of `family` that its final, audio-confirmed word list does
    not actually cover — recomputed from the words that survived, not from the
    candidates that were planned.
    """
    achieved: set[str] = set()
    for entry in words_out:
        achieved |= covered_phonemes(pronouncing_dict[entry['word']][0], family)

    return sorted(set(family['phonemes']) - achieved)


def escape_csharp(value: str) -> str:
    return value.replace('\\', '\\\\').replace('"', '\\"')


def render_csharp(families: list[dict]) -> str:
    lines = [
        '// <auto-generated>',
        '// Produced by generate_pronunciation_catalog.py. Do not hand-edit — rerun the script instead.',
        '// </auto-generated>',
        '',
        'namespace LanguageLab.Domain.Pronunciation;',
        '',
        'public static partial class PronunciationCatalog',
        '{',
        '    public static readonly IReadOnlyList<SoundFamily> Families = new List<SoundFamily>',
        '    {',
    ]

    for family in families:
        sounds = ', '.join(f'"{escape_csharp(s)}"' for s in family['target_sounds'])
        lines.append(f'        new("{escape_csharp(family["key"])}", "{escape_csharp(family["title"])}", new[] {{ {sounds} }}),')

    lines += [
        '    };',
        '',
        '    public static readonly IReadOnlyList<PronunciationWord> Words = new List<PronunciationWord>',
        '    {',
    ]

    for family in families:
        for word in family['words']:
            lines.append(
                '        new("{}", "{}", "{}", "{}", "{}"),'.format(
                    escape_csharp(word['word']),
                    escape_csharp(word['ipa']),
                    escape_csharp(family['key']),
                    escape_csharp(word['audio_us_file']),
                    escape_csharp(word['audio_uk_file']),
                )
            )

    lines += ['    };', '}', '']
    return '\n'.join(lines)


def main() -> None:
    import nltk
    from nltk.corpus import brown, cmudict

    nltk.download('cmudict', quiet=True)
    nltk.download('brown', quiet=True)

    from collections import Counter

    pronouncing_dict = cmudict.dict()
    frequency = Counter(w.lower() for w in brown.words())

    families = build_catalog(pronouncing_dict, frequency)

    CATALOG_OUTPUT.write_text(render_csharp(families))

    total_words = sum(len(f['words']) for f in families)
    total_families = len(families)
    empty_families = [f['key'] for f in families if not f['words']]
    print(f'Wrote {total_words} words across {total_families} families to {CATALOG_OUTPUT}')

    for family in families:
        words = ', '.join(f'{w["word"]} {w["ipa"]}' for w in family['words']) or '(none)'
        print(f'  {family["key"]}: {words}')

    if empty_families:
        print(f'WARNING: these families got no words at all: {empty_families}')

    incomplete = {f['key']: f['missing_phonemes'] for f in families if f['missing_phonemes']}
    if incomplete:
        print(f'WARNING: families missing target-sound coverage: {incomplete}')


if __name__ == '__main__':
    main()
