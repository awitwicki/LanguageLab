"""Builds the IPA reference catalog behind the alphabet screen: resolves a recording
for every symbol of the International Phonetic Alphabet and emits
LanguageLab.Domain/Pronunciation/IpaCatalog.Generated.cs.

Two recordings per symbol, both optional:
  * the sound on its own, from Commons' per-sound files ("Voiced velar fricative.ogg");
  * the example word, from its Wiktionary entry — reusing a clip the pronunciation
    trainer already committed when the example is one of its words.

SYMBOLS below is the hand-curated source of truth — the chart's letters with their
phonetic names, a plain-language hint, and an example word. Only the audio filenames
come from the network, and only a file that downloaded is written to the catalog, so a
symbol whose recording cannot be found stays silent rather than naming a missing file.

Run with: uv run --with requests generate_ipa_catalog.py
Requires network access (en.wiktionary.org, commons.wikimedia.org). Re-running is cheap:
a clip already on disk is kept and never downloaded twice.
"""

import re
import time
import unicodedata
from pathlib import Path

AUDIO_DIR = Path('web/public/pronunciation-audio')
CATALOG_OUTPUT = Path('LanguageLab.Domain/Pronunciation/IpaCatalog.Generated.cs')

# The chart's own divisions, in the order the screen shows them. `note` is the one line
# under the section heading that says what the group has in common.
SECTIONS = [
    {
        'key': 'pulmonic',
        'title': 'Pulmonic consonants',
        'note': 'Made with air pushed out of the lungs — every consonant English uses is here.',
    },
    {
        'key': 'non-pulmonic',
        'title': 'Non-pulmonic consonants',
        'note': 'Made without lung air: clicks, implosives and ejectives. No English word uses one.',
    },
    {
        'key': 'other',
        'title': 'Other symbols',
        'note': 'Sounds made at two places at once, and the few the main grid has no cell for.',
    },
    {
        'key': 'vowels',
        'title': 'Vowels',
        'note': 'Placed by how high the tongue sits and how far forward it is, rounded lips or not.',
    },
    {
        'key': 'diphthongs',
        'title': 'English diphthongs',
        'note': 'Not letters of the chart but pairs of them — one vowel gliding into another.',
    },
    {
        'key': 'marks',
        'title': 'Marks and stress',
        'note': 'Not sounds themselves: they change how the letter beside them is read.',
    },
]

# One row per symbol. `english` marks a sound English uses, which drives the "only
# English sounds" filter. `aliases` are the other spellings a learner may look up — the
# long vowels a dictionary writes with a length mark (iː for i), the r-coloured schwas
# (ɚ, ɝ) — and they are also how a symbol is matched to a trainer family's target sound.
# A row with no `word` is one the literature cites no settled example word for; its
# hint describes the sound instead, and the sound's own recording carries the row.
SYMBOLS = [
    # ---- Pulmonic consonants -------------------------------------------------
    # Plosive
    {'symbol': 'p', 'name': 'voiceless bilabial plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'p as in "pen"', 'word': 'pen', 'language': 'English', 'lang_code': 'en', 'ipa': '/pɛn/',
     'english': True},
    {'symbol': 'b', 'name': 'voiced bilabial plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'b as in "big"', 'word': 'big', 'language': 'English', 'lang_code': 'en', 'ipa': '/bɪɡ/',
     'english': True},
    {'symbol': 't', 'name': 'voiceless alveolar plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 't as in "ten"', 'word': 'ten', 'language': 'English', 'lang_code': 'en', 'ipa': '/tɛn/',
     'english': True},
    {'symbol': 'd', 'name': 'voiced alveolar plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'd as in "day"', 'word': 'day', 'language': 'English', 'lang_code': 'en', 'ipa': '/deɪ/',
     'english': True},
    {'symbol': 'ʈ', 'name': 'voiceless retroflex plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'a t said with the tongue tip curled back', 'word': 'टमाटर', 'language': 'Hindi',
     'lang_code': 'hi', 'ipa': '/ʈəmaːʈəɾ/'},
    {'symbol': 'ɖ', 'name': 'voiced retroflex plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'a d said with the tongue tip curled back', 'word': 'डाल', 'language': 'Hindi',
     'lang_code': 'hi', 'ipa': '/ɖaːl/'},
    {'symbol': 'c', 'name': 'voiceless palatal plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'a k made far forward, against the hard palate', 'word': 'tyúk', 'language': 'Hungarian',
     'lang_code': 'hu', 'ipa': '/ˈcuːk/'},
    {'symbol': 'ɟ', 'name': 'voiced palatal plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'the voiced twin of c', 'word': 'egy', 'language': 'Hungarian', 'lang_code': 'hu',
     'ipa': '/ɛɟ/'},
    {'symbol': 'k', 'name': 'voiceless velar plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'c as in "cat"', 'word': 'cat', 'language': 'English', 'lang_code': 'en', 'ipa': '/kæt/',
     'english': True},
    {'symbol': 'ɡ', 'name': 'voiced velar plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'g as in "go"', 'word': 'go', 'language': 'English', 'lang_code': 'en', 'ipa': '/ɡoʊ/',
     'english': True, 'aliases': ['g']},
    {'symbol': 'q', 'name': 'voiceless uvular plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'a k made deep at the back of the throat', 'word': 'قلب', 'language': 'Arabic',
     'lang_code': 'ar', 'ipa': '/qalb/'},
    {'symbol': 'ɢ', 'name': 'voiced uvular plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'the voiced twin of q', 'word': 'قند', 'language': 'Persian', 'lang_code': 'fa',
     'ipa': '/ɢand/'},
    {'symbol': 'ʔ', 'name': 'glottal plosive', 'section': 'pulmonic', 'group': 'Plosive',
     'hint': 'the catch in the throat between the halves of "uh-oh"', 'word': 'uh-oh',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ˈʌʔoʊ/', 'english': True},
    # Nasal
    {'symbol': 'm', 'name': 'bilabial nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'm as in "man"', 'word': 'man', 'language': 'English', 'lang_code': 'en', 'ipa': '/ˈmæn/',
     'english': True},
    {'symbol': 'ɱ', 'name': 'labiodental nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'an m with the lip against the teeth, as the m in "symphony"', 'word': 'symphony',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ˈsɪɱfəni/', 'english': True},
    {'symbol': 'n', 'name': 'alveolar nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'n as in "no"', 'word': 'no', 'language': 'English', 'lang_code': 'en', 'ipa': '/noʊ/',
     'english': True},
    {'symbol': 'ɳ', 'name': 'retroflex nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'an n with the tongue curled back', 'word': 'barn', 'language': 'Swedish',
     'lang_code': 'sv', 'ipa': '/bɑːɳ/'},
    {'symbol': 'ɲ', 'name': 'palatal nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'ny as in "canyon"', 'word': 'año', 'language': 'Spanish', 'lang_code': 'es',
     'ipa': '/ˈaɲo/'},
    {'symbol': 'ŋ', 'name': 'velar nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'ng as in "long"', 'word': 'long', 'language': 'English', 'lang_code': 'en',
     'ipa': '/lɑŋ/', 'english': True},
    {'symbol': 'ɴ', 'name': 'uvular nasal', 'section': 'pulmonic', 'group': 'Nasal',
     'hint': 'an n made right at the back, as Japanese ん before a pause', 'word': 'さん',
     'language': 'Japanese', 'lang_code': 'ja', 'ipa': '/saɴ/'},
    # Trill
    {'symbol': 'ʙ', 'name': 'bilabial trill', 'section': 'pulmonic', 'group': 'Trill',
     'hint': 'the lips buzzing together — a shivering "brrr"'},
    {'symbol': 'r', 'name': 'alveolar trill', 'section': 'pulmonic', 'group': 'Trill',
     'hint': 'a rolled r, as Ukrainian р', 'word': 'рука', 'language': 'Ukrainian', 'lang_code': 'uk',
     'ipa': '/rʊˈka/'},
    {'symbol': 'ʀ', 'name': 'uvular trill', 'section': 'pulmonic', 'group': 'Trill',
     'hint': 'an r rolled at the back of the throat', 'word': 'rot', 'language': 'German',
     'lang_code': 'de', 'ipa': '/ʀoːt/'},
    # Tap or flap
    {'symbol': 'ⱱ', 'name': 'labiodental flap', 'section': 'pulmonic', 'group': 'Tap or flap',
     'hint': 'the lower lip flicked outwards past the teeth'},
    {'symbol': 'ɾ', 'name': 'alveolar tap', 'section': 'pulmonic', 'group': 'Tap or flap',
     'hint': 'a single quick r, as the r in Spanish "caro"', 'word': 'caro', 'language': 'Spanish',
     'lang_code': 'es', 'ipa': '/ˈkaɾo/'},
    {'symbol': 'ɽ', 'name': 'retroflex flap', 'section': 'pulmonic', 'group': 'Tap or flap',
     'hint': 'the tongue curled back, then flicked forward', 'word': 'बड़ा', 'language': 'Hindi',
     'lang_code': 'hi', 'ipa': '/bəɽaː/'},
    # Fricative
    {'symbol': 'ɸ', 'name': 'voiceless bilabial fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'blowing out a candle — an f made with both lips', 'word': 'ふ', 'language': 'Japanese',
     'lang_code': 'ja', 'ipa': '/ɸɯ/'},
    {'symbol': 'β', 'name': 'voiced bilabial fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'the voiced twin of ɸ, as Spanish b between vowels', 'word': 'lavar',
     'language': 'Spanish', 'lang_code': 'es', 'ipa': '/laˈβaɾ/'},
    {'symbol': 'f', 'name': 'voiceless labiodental fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'f as in "fish"', 'word': 'fish', 'language': 'English', 'lang_code': 'en', 'ipa': '/fɪʃ/',
     'english': True},
    {'symbol': 'v', 'name': 'voiced labiodental fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'v as in "very"', 'word': 'very', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ˈvɛɹi/', 'english': True},
    {'symbol': 'θ', 'name': 'voiceless dental fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'th as in "think"', 'word': 'think', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ˈθɪŋk/', 'english': True},
    {'symbol': 'ð', 'name': 'voiced dental fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'th as in "other"', 'word': 'other', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ˈʌðɚ/', 'english': True},
    {'symbol': 's', 'name': 'voiceless alveolar fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 's as in "said"', 'word': 'said', 'language': 'English', 'lang_code': 'en', 'ipa': '/sɛd/',
     'english': True},
    {'symbol': 'z', 'name': 'voiced alveolar fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'z as in "zoo"', 'word': 'zoo', 'language': 'English', 'lang_code': 'en', 'ipa': '/zuː/',
     'english': True},
    {'symbol': 'ʃ', 'name': 'voiceless postalveolar fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'sh as in "ship"', 'word': 'ship', 'language': 'English', 'lang_code': 'en', 'ipa': '/ʃɪp/',
     'english': True},
    {'symbol': 'ʒ', 'name': 'voiced postalveolar fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 's as in "measure"', 'word': 'measure', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ˈmɛʒɚ/', 'english': True},
    {'symbol': 'ʂ', 'name': 'voiceless retroflex fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'a sh with the tongue curled back, as Mandarin sh', 'word': '上', 'language': 'Mandarin',
     'lang_code': 'zh', 'ipa': '/ʂâŋ/'},
    {'symbol': 'ʐ', 'name': 'voiced retroflex fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'the voiced twin of ʂ, as Mandarin r', 'word': '人', 'language': 'Mandarin',
     'lang_code': 'zh', 'ipa': '/ʐə̌n/'},
    {'symbol': 'ç', 'name': 'voiceless palatal fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'ch as in German "ich" — a whispered y', 'word': 'ich', 'language': 'German',
     'lang_code': 'de', 'ipa': '/ɪç/'},
    {'symbol': 'ʝ', 'name': 'voiced palatal fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'the voiced twin of ç', 'word': 'ayudar', 'language': 'Spanish', 'lang_code': 'es',
     'ipa': '/aʝuˈðaɾ/'},
    {'symbol': 'x', 'name': 'voiceless velar fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'as Ukrainian х — a rasped k', 'word': 'хата', 'language': 'Ukrainian', 'lang_code': 'uk',
     'ipa': '/ˈxata/'},
    {'symbol': 'ɣ', 'name': 'voiced velar fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'the voiced twin of x, as Greek γ', 'word': 'γάλα', 'language': 'Greek',
     'lang_code': 'el', 'ipa': '/ˈɣala/'},
    {'symbol': 'χ', 'name': 'voiceless uvular fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'ch as in German "Bach", made deeper than x', 'word': 'Bach', 'language': 'German',
     'lang_code': 'de', 'ipa': '/baχ/'},
    {'symbol': 'ʁ', 'name': 'voiced uvular fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'the French r', 'word': 'rouge', 'language': 'French', 'lang_code': 'fr', 'ipa': '/ʁuʒ/'},
    {'symbol': 'ħ', 'name': 'voiceless pharyngeal fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'a tight h squeezed in the throat, as Arabic ح', 'word': 'حال', 'language': 'Arabic',
     'lang_code': 'ar', 'ipa': '/ħaːl/'},
    {'symbol': 'ʕ', 'name': 'voiced pharyngeal fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'the voiced twin of ħ, as Arabic ع', 'word': 'عين', 'language': 'Arabic',
     'lang_code': 'ar', 'ipa': '/ʕajn/'},
    {'symbol': 'h', 'name': 'voiceless glottal fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'h as in "how"', 'word': 'how', 'language': 'English', 'lang_code': 'en', 'ipa': '/haʊ/',
     'english': True},
    {'symbol': 'ɦ', 'name': 'voiced glottal fricative', 'section': 'pulmonic', 'group': 'Fricative',
     'hint': 'a breathy h with the voice on, as Ukrainian г', 'word': 'гора', 'language': 'Ukrainian',
     'lang_code': 'uk', 'ipa': '/ɦoˈra/'},
    # Lateral fricative
    {'symbol': 'ɬ', 'name': 'voiceless alveolar lateral fricative', 'section': 'pulmonic',
     'group': 'Lateral fricative', 'hint': 'an l hissed around the sides, as Welsh ll', 'word': 'llan',
     'language': 'Welsh', 'lang_code': 'cy', 'ipa': '/ɬan/'},
    {'symbol': 'ɮ', 'name': 'voiced alveolar lateral fricative', 'section': 'pulmonic',
     'group': 'Lateral fricative', 'hint': 'the voiced twin of ɬ', 'word': 'dlala', 'language': 'Zulu',
     'lang_code': 'zu', 'ipa': '/ɮaːla/'},
    # Approximant
    {'symbol': 'ʋ', 'name': 'labiodental approximant', 'section': 'pulmonic', 'group': 'Approximant',
     'hint': 'a v with the lips barely closed — between v and w', 'word': 'wang', 'language': 'Dutch',
     'lang_code': 'nl', 'ipa': '/ʋɑŋ/'},
    {'symbol': 'ɹ', 'name': 'alveolar approximant', 'section': 'pulmonic', 'group': 'Approximant',
     'hint': 'r as in "red" — the English r, tongue never touching', 'word': 'red',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ɹɛd/', 'english': True, 'aliases': ['r']},
    {'symbol': 'ɻ', 'name': 'retroflex approximant', 'section': 'pulmonic', 'group': 'Approximant',
     'hint': 'an English-like r with the tongue curled further back', 'word': '日',
     'language': 'Mandarin', 'lang_code': 'zh', 'ipa': '/ɻî/'},
    {'symbol': 'j', 'name': 'palatal approximant', 'section': 'pulmonic', 'group': 'Approximant',
     'hint': 'y as in "yes"', 'word': 'yes', 'language': 'English', 'lang_code': 'en', 'ipa': '/jɛs/',
     'english': True},
    {'symbol': 'ɰ', 'name': 'velar approximant', 'section': 'pulmonic', 'group': 'Approximant',
     'hint': 'a w without the lip rounding', 'word': 'agua', 'language': 'Spanish', 'lang_code': 'es',
     'ipa': '/ˈaɰwa/'},
    # Lateral approximant
    {'symbol': 'l', 'name': 'alveolar lateral approximant', 'section': 'pulmonic',
     'group': 'Lateral approximant', 'hint': 'l as in "little"', 'word': 'little', 'language': 'English',
     'lang_code': 'en', 'ipa': '/ˈlɪtl̩/', 'english': True},
    {'symbol': 'ɭ', 'name': 'retroflex lateral approximant', 'section': 'pulmonic',
     'group': 'Lateral approximant', 'hint': 'an l with the tongue curled back', 'word': 'sorl',
     'language': 'Swedish', 'lang_code': 'sv', 'ipa': '/sɔːɭ/'},
    {'symbol': 'ʎ', 'name': 'palatal lateral approximant', 'section': 'pulmonic',
     'group': 'Lateral approximant', 'hint': 'lli as in "million", as Italian gli', 'word': 'gli',
     'language': 'Italian', 'lang_code': 'it', 'ipa': '/ʎi/'},
    {'symbol': 'ʟ', 'name': 'velar lateral approximant', 'section': 'pulmonic',
     'group': 'Lateral approximant', 'hint': 'an l made at the back of the mouth'},
    # ---- Non-pulmonic consonants --------------------------------------------
    {'symbol': 'ʘ', 'name': 'bilabial click', 'section': 'non-pulmonic', 'group': 'Click',
     'hint': 'a lip smack, like a kiss'},
    {'symbol': 'ǀ', 'name': 'dental click', 'section': 'non-pulmonic', 'group': 'Click',
     'hint': 'the "tsk tsk" of disapproval'},
    {'symbol': 'ǃ', 'name': 'alveolar click', 'section': 'non-pulmonic', 'group': 'Click',
     'hint': 'a hollow pop, like a cork', 'word': 'Xhosa', 'language': 'Xhosa', 'lang_code': 'xh',
     'ipa': '/ˈkǁʰɔsa/', 'sound_file': 'File:Postalveolar click.ogg'},
    {'symbol': 'ǂ', 'name': 'palatoalveolar click', 'section': 'non-pulmonic', 'group': 'Click',
     'hint': 'a click made with the tongue flat against the palate'},
    {'symbol': 'ǁ', 'name': 'alveolar lateral click', 'section': 'non-pulmonic', 'group': 'Click',
     'hint': 'the click used to urge a horse on'},
    {'symbol': 'ɓ', 'name': 'voiced bilabial implosive', 'section': 'non-pulmonic', 'group': 'Implosive',
     'hint': 'a b swallowed inwards, air pulled in'},
    {'symbol': 'ɗ', 'name': 'voiced alveolar implosive', 'section': 'non-pulmonic', 'group': 'Implosive',
     'hint': 'a d swallowed inwards'},
    {'symbol': 'ʄ', 'name': 'voiced palatal implosive', 'section': 'non-pulmonic', 'group': 'Implosive',
     'hint': 'a ɟ swallowed inwards'},
    {'symbol': 'ɠ', 'name': 'voiced velar implosive', 'section': 'non-pulmonic', 'group': 'Implosive',
     'hint': 'a g swallowed inwards'},
    {'symbol': 'ʛ', 'name': 'voiced uvular implosive', 'section': 'non-pulmonic', 'group': 'Implosive',
     'hint': 'the deepest implosive, made at the uvula'},
    {'symbol': 'ʼ', 'name': 'ejective mark', 'section': 'non-pulmonic', 'group': 'Ejective',
     'hint': 'written after a consonant (pʼ, tʼ, kʼ): popped out with the throat closed'},
    # ---- Other symbols ------------------------------------------------------
    {'symbol': 'ʍ', 'name': 'voiceless labial-velar fricative', 'section': 'other', 'group': 'Other',
     'hint': 'wh as in "which", for speakers who keep it apart from "witch"', 'word': 'which',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ʍɪtʃ/', 'english': True},
    {'symbol': 'w', 'name': 'voiced labial-velar approximant', 'section': 'other', 'group': 'Other',
     'hint': 'w as in "when"', 'word': 'when', 'language': 'English', 'lang_code': 'en', 'ipa': '/wɛn/',
     'english': True},
    {'symbol': 'ɥ', 'name': 'voiced labial-palatal approximant', 'section': 'other', 'group': 'Other',
     'hint': 'a y said with rounded lips, as French "huit"', 'word': 'huit', 'language': 'French',
     'lang_code': 'fr', 'ipa': '/ɥit/', 'sound_file': 'File:LL-Q150 (fra)-WikiLucas00-IPA ɥ.wav'},
    {'symbol': 'ʜ', 'name': 'voiceless epiglottal fricative', 'section': 'other', 'group': 'Other',
     'hint': 'a rasping h deep in the throat'},
    {'symbol': 'ʢ', 'name': 'voiced epiglottal fricative', 'section': 'other', 'group': 'Other',
     'hint': 'the voiced twin of ʜ'},
    {'symbol': 'ʡ', 'name': 'epiglottal plosive', 'section': 'other', 'group': 'Other',
     'hint': 'a catch made lower than ʔ, in the throat itself',
     'sound_file': 'File:Epiglottal stop.ogg'},
    {'symbol': 'ɕ', 'name': 'voiceless alveolo-palatal fricative', 'section': 'other', 'group': 'Other',
     'hint': 'a sh with the tongue spread towards the palate, as Mandarin x', 'word': '西',
     'language': 'Mandarin', 'lang_code': 'zh', 'ipa': '/ɕi/'},
    {'symbol': 'ʑ', 'name': 'voiced alveolo-palatal fricative', 'section': 'other', 'group': 'Other',
     'hint': 'the voiced twin of ɕ, as Polish ź', 'word': 'źle', 'language': 'Polish',
     'lang_code': 'pl', 'ipa': '/ʑlɛ/'},
    {'symbol': 'ɺ', 'name': 'alveolar lateral flap', 'section': 'other', 'group': 'Other',
     'hint': 'an l flicked once, halfway to an r'},
    {'symbol': 'ɧ', 'name': 'simultaneous ʃ and x', 'section': 'other', 'group': 'Other',
     'hint': 'the Swedish sj-sound', 'word': 'sjuk', 'language': 'Swedish', 'lang_code': 'sv',
     'ipa': '/ɧʉːk/'},
    # ---- Vowels -------------------------------------------------------------
    {'symbol': 'i', 'name': 'close front unrounded vowel', 'section': 'vowels', 'group': 'Close',
     'hint': 'ee as in "sheep"', 'word': 'sheep', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ʃiːp/', 'english': True, 'aliases': ['iː', 'i:']},
    {'symbol': 'y', 'name': 'close front rounded vowel', 'section': 'vowels', 'group': 'Close',
     'hint': 'an ee said with rounded lips, as French u', 'word': 'rue', 'language': 'French',
     'lang_code': 'fr', 'ipa': '/ʁy/'},
    {'symbol': 'ɨ', 'name': 'close central unrounded vowel', 'section': 'vowels', 'group': 'Close',
     'hint': 'as Polish y — between ee and oo, lips flat', 'word': 'ryba', 'language': 'Polish',
     'lang_code': 'pl', 'ipa': '/ˈrɨba/'},
    {'symbol': 'ʉ', 'name': 'close central rounded vowel', 'section': 'vowels', 'group': 'Close',
     'hint': 'the rounded twin of ɨ, as Swedish u', 'word': 'hus', 'language': 'Swedish',
     'lang_code': 'sv', 'ipa': '/hʉːs/'},
    {'symbol': 'ɯ', 'name': 'close back unrounded vowel', 'section': 'vowels', 'group': 'Close',
     'hint': 'an oo with the lips unrounded, as Turkish ı', 'word': 'kız', 'language': 'Turkish',
     'lang_code': 'tr', 'ipa': '/kɯz/'},
    {'symbol': 'u', 'name': 'close back rounded vowel', 'section': 'vowels', 'group': 'Close',
     'hint': 'oo as in "food"', 'word': 'food', 'language': 'English', 'lang_code': 'en',
     'ipa': '/fuːd/', 'english': True, 'aliases': ['uː', 'u:']},
    {'symbol': 'ɪ', 'name': 'near-close near-front unrounded vowel', 'section': 'vowels',
     'group': 'Near-close', 'hint': 'i as in "big" — shorter and slacker than ee', 'word': 'big',
     'language': 'English', 'lang_code': 'en', 'ipa': '/bɪɡ/', 'english': True},
    {'symbol': 'ʏ', 'name': 'near-close near-front rounded vowel', 'section': 'vowels',
     'group': 'Near-close', 'hint': 'the rounded twin of ɪ, as German ü in "hübsch"', 'word': 'hübsch',
     'language': 'German', 'lang_code': 'de', 'ipa': '/hʏpʃ/'},
    {'symbol': 'ʊ', 'name': 'near-close near-back rounded vowel', 'section': 'vowels',
     'group': 'Near-close', 'hint': 'oo as in "good" — shorter than in "food"', 'word': 'good',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ɡʊd/', 'english': True},
    # British dictionaries write the "bed" vowel /e/ where American ones write /ɛ/, so a
    # learner meets both for the same word — hence an English example on this one too.
    {'symbol': 'e', 'name': 'close-mid front unrounded vowel', 'section': 'vowels', 'group': 'Close-mid',
     'hint': 'e as in British "bed"; on its own, a pure ay with no glide, as French é',
     'word': 'bed', 'language': 'English', 'lang_code': 'en', 'ipa': '/bed/', 'english': True},
    {'symbol': 'ø', 'name': 'close-mid front rounded vowel', 'section': 'vowels', 'group': 'Close-mid',
     'hint': 'the rounded twin of e, as French eu', 'word': 'bleu', 'language': 'French',
     'lang_code': 'fr', 'ipa': '/blø/'},
    {'symbol': 'ɘ', 'name': 'close-mid central unrounded vowel', 'section': 'vowels', 'group': 'Close-mid',
     'hint': 'a schwa said a little higher'},
    {'symbol': 'ɵ', 'name': 'close-mid central rounded vowel', 'section': 'vowels', 'group': 'Close-mid',
     'hint': 'the rounded twin of ɘ, as Dutch u in "hut"', 'word': 'hut', 'language': 'Dutch',
     'lang_code': 'nl', 'ipa': '/hɵt/'},
    {'symbol': 'ɤ', 'name': 'close-mid back unrounded vowel', 'section': 'vowels', 'group': 'Close-mid',
     'hint': 'an o with the lips unrounded, as Mandarin e', 'word': '哥', 'language': 'Mandarin',
     'lang_code': 'zh', 'ipa': '/kɤ/'},
    {'symbol': 'o', 'name': 'close-mid back rounded vowel', 'section': 'vowels', 'group': 'Close-mid',
     'hint': 'a pure o with no glide, as French eau', 'word': 'eau', 'language': 'French',
     'lang_code': 'fr', 'ipa': '/o/'},
    {'symbol': 'ə', 'name': 'mid central vowel (schwa)', 'section': 'vowels', 'group': 'Mid',
     'hint': 'the unstressed a in "people" — the most common English vowel', 'word': 'people',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ˈpiːpəl/', 'english': True,
     'aliases': ['ɚ'], 'sound_file': 'File:Mid-central vowel.ogg'},
    {'symbol': 'ɛ', 'name': 'open-mid front unrounded vowel', 'section': 'vowels', 'group': 'Open-mid',
     'hint': 'e as in "said"', 'word': 'said', 'language': 'English', 'lang_code': 'en', 'ipa': '/sɛd/',
     'english': True},
    {'symbol': 'œ', 'name': 'open-mid front rounded vowel', 'section': 'vowels', 'group': 'Open-mid',
     'hint': 'the rounded twin of ɛ, as French œu', 'word': 'sœur', 'language': 'French',
     'lang_code': 'fr', 'ipa': '/sœʁ/'},
    {'symbol': 'ɜ', 'name': 'open-mid central unrounded vowel', 'section': 'vowels', 'group': 'Open-mid',
     'hint': 'ur as in British "bird"', 'word': 'bird', 'language': 'English', 'lang_code': 'en',
     'ipa': '/bɜːd/', 'english': True, 'aliases': ['ɜː', 'ɝ', 'ɜ:']},
    {'symbol': 'ɞ', 'name': 'open-mid central rounded vowel', 'section': 'vowels', 'group': 'Open-mid',
     'hint': 'the rounded twin of ɜ'},
    {'symbol': 'ʌ', 'name': 'open-mid back unrounded vowel', 'section': 'vowels', 'group': 'Open-mid',
     'hint': 'u as in "cup"', 'word': 'cup', 'language': 'English', 'lang_code': 'en', 'ipa': '/kʌp/',
     'english': True},
    {'symbol': 'ɔ', 'name': 'open-mid back rounded vowel', 'section': 'vowels', 'group': 'Open-mid',
     'hint': 'aw as in "always"', 'word': 'always', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ˈɔlweɪz/', 'english': True, 'aliases': ['ɔː', 'ɔ:']},
    {'symbol': 'æ', 'name': 'near-open front unrounded vowel', 'section': 'vowels', 'group': 'Near-open',
     'hint': 'a as in "man"', 'word': 'man', 'language': 'English', 'lang_code': 'en', 'ipa': '/ˈmæn/',
     'english': True},
    {'symbol': 'ɐ', 'name': 'near-open central vowel', 'section': 'vowels', 'group': 'Near-open',
     'hint': 'a slack a, as the -er ending of German "besser"', 'word': 'besser', 'language': 'German',
     'lang_code': 'de', 'ipa': '/ˈbɛsɐ/'},
    {'symbol': 'a', 'name': 'open front unrounded vowel', 'section': 'vowels', 'group': 'Open',
     'hint': 'a bright, open a, as Ukrainian а', 'word': 'мама', 'language': 'Ukrainian',
     'lang_code': 'uk', 'ipa': '/ˈmama/'},
    {'symbol': 'ɶ', 'name': 'open front rounded vowel', 'section': 'vowels', 'group': 'Open',
     'hint': 'the rounded twin of a — no language is known to use it as a plain vowel'},
    {'symbol': 'ɑ', 'name': 'open back unrounded vowel', 'section': 'vowels', 'group': 'Open',
     'hint': 'a as in American "long"', 'word': 'long', 'language': 'English', 'lang_code': 'en',
     'ipa': '/lɑŋ/', 'english': True, 'aliases': ['ɑː', 'ɑ:']},
    {'symbol': 'ɒ', 'name': 'open back rounded vowel', 'section': 'vowels', 'group': 'Open',
     'hint': 'o as in British "hot"', 'word': 'hot', 'language': 'English', 'lang_code': 'en',
     'ipa': '/hɒt/', 'english': True},
    # ---- English diphthongs -------------------------------------------------
    {'symbol': 'eɪ', 'name': 'a glide from e to ɪ', 'section': 'diphthongs', 'group': 'Closing',
     'hint': 'ay as in "day"', 'word': 'day', 'language': 'English', 'lang_code': 'en', 'ipa': '/deɪ/',
     'english': True},
    {'symbol': 'aɪ', 'name': 'a glide from a to ɪ', 'section': 'diphthongs', 'group': 'Closing',
     'hint': 'i as in "time"', 'word': 'time', 'language': 'English', 'lang_code': 'en', 'ipa': '/taɪm/',
     'english': True},
    {'symbol': 'ɔɪ', 'name': 'a glide from ɔ to ɪ', 'section': 'diphthongs', 'group': 'Closing',
     'hint': 'oy as in "boy"', 'word': 'boy', 'language': 'English', 'lang_code': 'en', 'ipa': '/bɔɪ/',
     'english': True},
    {'symbol': 'aʊ', 'name': 'a glide from a to ʊ', 'section': 'diphthongs', 'group': 'Closing',
     'hint': 'ow as in "how"', 'word': 'how', 'language': 'English', 'lang_code': 'en', 'ipa': '/haʊ/',
     'english': True},
    {'symbol': 'oʊ', 'name': 'a glide from o to ʊ', 'section': 'diphthongs', 'group': 'Closing',
     'hint': 'o as in "go"', 'word': 'go', 'language': 'English', 'lang_code': 'en', 'ipa': '/ɡoʊ/',
     'english': True, 'aliases': ['əʊ']},
    {'symbol': 'ɪə', 'name': 'a glide from ɪ to ə', 'section': 'diphthongs', 'group': 'Centring',
     'hint': 'ear as in British "here"', 'word': 'here', 'language': 'English', 'lang_code': 'en',
     'ipa': '/hɪə/', 'english': True},
    {'symbol': 'eə', 'name': 'a glide from e to ə', 'section': 'diphthongs', 'group': 'Centring',
     'hint': 'air as in British "there"', 'word': 'there', 'language': 'English', 'lang_code': 'en',
     'ipa': '/ðeə/', 'english': True},
    {'symbol': 'ʊə', 'name': 'a glide from ʊ to ə', 'section': 'diphthongs', 'group': 'Centring',
     'hint': 'oor as in British "tour"', 'word': 'tour', 'language': 'English', 'lang_code': 'en',
     'ipa': '/tʊə/', 'english': True},
    # ---- Marks and stress ---------------------------------------------------
    {'symbol': 'ˈ', 'name': 'primary stress', 'section': 'marks', 'group': 'Stress',
     'hint': 'the syllable after it is the loud one', 'word': 'begin', 'language': 'English',
     'lang_code': 'en', 'ipa': '/bɪˈɡɪn/', 'english': True},
    {'symbol': 'ˌ', 'name': 'secondary stress', 'section': 'marks', 'group': 'Stress',
     'hint': 'a weaker beat than ˈ, but stronger than none', 'word': 'understand',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ˌʌndəˈstænd/', 'english': True},
    {'symbol': 'ː', 'name': 'length mark', 'section': 'marks', 'group': 'Length',
     'hint': 'the vowel before it is held longer', 'word': 'sheep', 'language': 'English',
     'lang_code': 'en', 'ipa': '/ʃiːp/', 'english': True, 'aliases': [':']},
    {'symbol': 'ʰ', 'name': 'aspiration', 'section': 'marks', 'group': 'Quality',
     'hint': 'a puff of air after the consonant, as English p at the start of a word', 'word': 'pin',
     'language': 'English', 'lang_code': 'en', 'ipa': '/pʰɪn/', 'english': True},
    {'symbol': '̃', 'name': 'nasalization', 'section': 'marks', 'group': 'Quality',
     'hint': 'the vowel is said through the nose, as French on', 'word': 'bon', 'language': 'French',
     'lang_code': 'fr', 'ipa': '/bɔ̃/'},
    {'symbol': '̩', 'name': 'syllabic', 'section': 'marks', 'group': 'Quality',
     'hint': 'the consonant is a syllable of its own, with no vowel', 'word': 'little',
     'language': 'English', 'lang_code': 'en', 'ipa': '/ˈlɪtl̩/', 'english': True},
    {'symbol': '͡', 'name': 'tie bar', 'section': 'marks', 'group': 'Quality',
     'hint': 'the two letters under it are one sound, as the j in "job"', 'word': 'job',
     'language': 'English', 'lang_code': 'en', 'ipa': '/d͡ʒɑb/', 'english': True,
     'aliases': ['t͡ʃ', 'd͡ʒ', 'tʃ', 'dʒ']},
    {'symbol': '.', 'name': 'syllable break', 'section': 'marks', 'group': 'Length',
     'hint': 'where one syllable ends and the next begins', 'word': 'people', 'language': 'English',
     'lang_code': 'en', 'ipa': '/ˈpiː.pəl/', 'english': True},
]

AUDIO_EXTENSIONS = ('.ogg', '.oga', '.wav', '.mp3', '.flac')


def slugify(value: str) -> str:
    """A filename-safe, ASCII-only slug: accents folded away, everything else dropped."""
    folded = unicodedata.normalize('NFKD', value)
    ascii_only = ''.join(c for c in folded if not unicodedata.combining(c))
    lowered = re.sub(r'[^a-z0-9]+', '-', ascii_only.lower())
    return lowered.strip('-')


def sound_title_candidates(name: str) -> list[str]:
    """Commons titles that may hold the sound on its own, best guess first.

    Commons names these files after the phonetic name ("Voiceless palatal
    fricative.ogg"), but leaves "voiced" off the sounds that are voiced by default —
    trills, nasals, flaps and approximants — so both spellings have to be tried.
    """
    names = [name]
    if name.startswith('voiced '):
        names.append(name[len('voiced '):])

    seen: set[str] = set()
    candidates = []
    for stem in names:
        capitalized = stem[:1].upper() + stem[1:]
        for extension in AUDIO_EXTENSIONS:
            title = f'File:{capitalized}{extension}'
            if title not in seen:
                seen.add(title)
                candidates.append(title)
    return candidates


def local_sound_filename(name: str, extension: str) -> str:
    return f'ipa-{slugify(name)}{extension}'


def local_word_filename(word: str, lang_code: str, extension: str, fallback_slug: str) -> str:
    """Where an example word's clip is stored. A word in a non-Latin script slugifies to
    nothing, so those are named after the sound instead."""
    slug = slugify(word) or f'{fallback_slug}-word'
    return f'{slug}-{lang_code}{extension}'


def commons_title(filename: str) -> str:
    """A filename as Commons titles it: File-qualified, first letter capital, spaces for
    underscores. Wiktionary writes {{audio|en|en-us-tour.ogg}} while the file itself is
    "File:En-us-tour.ogg", and the API answers under its own spelling — so both sides of
    the comparison have to be normalized or every lowercase name is missed."""
    bare = filename[len('File:'):] if filename.startswith('File:') else filename
    bare = bare.replace('_', ' ').lstrip()
    return 'File:' + bare[:1].upper() + bare[1:]


def audio_titles(image_titles: list[str]) -> list[str]:
    """The recordings among a page's files — charts, diagrams and photos dropped."""
    return [title for title in image_titles if title.lower().endswith(AUDIO_EXTENSIONS)]


def _significant_words(value: str) -> set[str]:
    return {word for word in re.split(r'[^a-z0-9]+', value.lower()) if len(word) > 1}


def best_sound_title(article_title: str, titles: list[str]) -> str | None:
    """The one recording among `titles` that is the sound itself rather than a word.

    A sound's Wikipedia article lists every example word it cites, in every language, so
    the file has to be picked by name: the sound's own recording is named after the sound
    ("Glottal stop.ogg"), while an example is named after its word ("Cs-naopak.ogg").
    Sharing a single word with the article title is not enough — "Vowel chart.ogg" shares
    "vowel" with every vowel there is — so two words are the floor. Among equals, the
    plainest name wins, and .ogg wins over the other formats.
    """
    wanted = _significant_words(article_title)
    scored = []
    for title in titles:
        stem = Path(title[len('File:'):]).stem
        overlap = len(wanted & _significant_words(stem))
        if overlap >= 2:
            scored.append((-overlap, not title.lower().endswith('.ogg'), len(stem), title))

    return min(scored)[3] if scored else None


AUDIO_TEMPLATE_RE = re.compile(r'\{\{audio\|([^|{}]+)\|([^|{}]+)')


def audio_filenames_for_language(wikitext: str, lang_code: str) -> list[str]:
    """Every {{audio|<lang>|File}} recording a Wiktionary page lists for one language."""
    return [
        filename.strip()
        for language, filename in AUDIO_TEMPLATE_RE.findall(wikitext)
        if language.strip() == lang_code and filename.strip()
    ]


def escape_csharp(value: str) -> str:
    return value.replace('\\', '\\\\').replace('"', '\\"')


def _csharp_string(value: str | None) -> str:
    return 'null' if value is None else f'"{escape_csharp(value)}"'


def render_csharp(sections: list[dict]) -> str:
    lines = [
        '// <auto-generated>',
        '// Produced by generate_ipa_catalog.py. Do not hand-edit — rerun the script instead.',
        '// </auto-generated>',
        '',
        'namespace LanguageLab.Domain.Pronunciation;',
        '',
        'public static partial class IpaCatalog',
        '{',
        '    public static readonly IReadOnlyList<IpaSection> Sections = new List<IpaSection>',
        '    {',
    ]

    for section in sections:
        lines.append(
            '        new({}, {}, {}, new List<IpaEntry>'.format(
                _csharp_string(section['key']),
                _csharp_string(section['title']),
                _csharp_string(section['note']),
            )
        )
        lines.append('        {')
        for entry in section['entries']:
            aliases = entry.get('aliases') or []
            rendered_aliases = (
                'new string[0]'
                if not aliases
                else 'new[] { ' + ', '.join(f'"{escape_csharp(a)}"' for a in aliases) + ' }'
            )
            lines.append(
                '            new({}, {}, {}, {}, {}, {}, {}, {}, {}, {}, {}),'.format(
                    _csharp_string(entry['symbol']),
                    _csharp_string(entry['name']),
                    _csharp_string(entry['hint']),
                    _csharp_string(entry['group']),
                    'true' if entry.get('english') else 'false',
                    _csharp_string(entry.get('word')),
                    _csharp_string(entry.get('language')),
                    _csharp_string(entry.get('ipa')),
                    _csharp_string(entry.get('sound_audio_file')),
                    _csharp_string(entry.get('word_audio_file')),
                    rendered_aliases,
                )
            )
        lines.append('        }),')

    lines += ['    };', '}', '']
    return '\n'.join(lines)


# --- Everything below needs the network ------------------------------------------------

def _requests():
    import requests

    return requests


COMMONS_API = 'https://commons.wikimedia.org/w/api.php'
WIKTIONARY_API = 'https://en.wiktionary.org/w/api.php'
WIKIPEDIA_API = 'https://en.wikipedia.org/w/api.php'

# Wikimedia rejects requests without a descriptive User-Agent (bot policy:
# https://meta.wikimedia.org/wiki/User-Agent_policy) — the same header the
# pronunciation-catalog generator needs.
HTTP_HEADERS = {
    'User-Agent': 'LanguageLab-IpaCatalog/1.0 '
    '(https://github.com/awitwicki/LanguageLab; witwicki666@gmail.com)'
}


def api_get(api: str, params: dict) -> dict:
    """A MediaWiki call that retries, and gives up loudly.

    Wikimedia throttles a run this long, and a swallowed 429 is indistinguishable from
    "Commons has no such file" — which would quietly leave a third of the chart silent.
    A page that genuinely does not exist comes back 200 with `missing`, so raising here
    only ever means the network, never absent data.
    """
    delay = 1.0
    status = None
    for _ in range(5):
        response = _requests().get(
            api, params={**params, 'format': 'json'}, timeout=30, headers=HTTP_HEADERS
        )
        if response.status_code == 200:
            return response.json()
        status = response.status_code
        time.sleep(max(float(response.headers.get('Retry-After') or 0), delay))
        delay *= 2

    raise RuntimeError(f'{api} kept answering {status} — stopping rather than writing a silent catalog')


def resolve_commons_urls(titles: list[str]) -> dict[str, str]:
    """Direct download URLs for whichever of `titles` exist on Commons, 50 per request."""
    found: dict[str, str] = {}
    for start in range(0, len(titles), 50):
        chunk = titles[start:start + 50]
        if not chunk:
            continue
        data = api_get(COMMONS_API, {
            'action': 'query', 'prop': 'imageinfo', 'iiprop': 'url', 'titles': '|'.join(chunk),
        })
        for page in data.get('query', {}).get('pages', {}).values():
            if 'missing' in page or not page.get('imageinfo'):
                continue
            # Commons normalizes underscores to spaces; key by the title it gives back.
            found[page['title']] = page['imageinfo'][0]['url']

    return found


def fetch_article_audio(name: str) -> tuple[str, list[str]]:
    """The recordings listed on a sound's Wikipedia article, with the article's own title.

    Going through the article rather than guessing a Commons filename is what handles the
    names Commons spells its own way — "Glottal stop" for the glottal plosive, "sibilant"
    where the chart says fricative — because Wikipedia redirects the chart's name to it.
    """
    data = api_get(WIKIPEDIA_API, {
        'action': 'query', 'titles': name[:1].upper() + name[1:], 'redirects': 1,
        'prop': 'images', 'imlimit': 'max',
    })
    pages = data.get('query', {}).get('pages', {})
    for page in pages.values():
        if 'missing' in page:
            continue
        images = [image['title'] for image in page.get('images', [])]
        return page.get('title', name), audio_titles(images)

    return name, []


def fetch_wikitext(word: str) -> str:
    data = api_get(WIKTIONARY_API, {'action': 'parse', 'page': word, 'prop': 'wikitext'})
    if 'error' in data:
        return ''

    return data.get('parse', {}).get('wikitext', {}).get('*', '')


def download(url: str, path: Path) -> bool:
    if path.exists():
        return True

    delay = 1.0
    for _ in range(4):
        response = _requests().get(url, timeout=60, headers=HTTP_HEADERS)
        if response.status_code == 200 and response.content:
            path.write_bytes(response.content)
            time.sleep(0.2)
            return True
        if response.status_code == 404:
            return False
        time.sleep(delay)
        delay *= 2

    return False


def existing_trainer_clip(word: str) -> str | None:
    """A recording the pronunciation trainer already committed for this word — the US
    accent by preference, so the alphabet adds no second copy of a clip in the tree."""
    for accent in ('us', 'uk'):
        for extension in AUDIO_EXTENSIONS:
            candidate = AUDIO_DIR / f'{word}-{accent}{extension}'
            if candidate.exists():
                return candidate.name
    return None


def resolve_sound_clip(entry: dict) -> str | None:
    """The sound on its own: the file Commons names after it, or failing that the one its
    Wikipedia article carries."""
    candidates = sound_title_candidates(entry['name'])
    if entry.get('sound_file'):
        candidates = [commons_title(entry['sound_file'])] + candidates
    urls = resolve_commons_urls(candidates)
    ordered = [title for title in candidates if title in urls]

    if not ordered:
        article_title, listed = fetch_article_audio(entry['name'])
        chosen = best_sound_title(article_title, listed)
        if chosen:
            urls = resolve_commons_urls([chosen])
            ordered = [title for title in [chosen] if title in urls]

    for title in ordered:
        filename = local_sound_filename(entry['name'], Path(title).suffix)
        if download(urls[title], AUDIO_DIR / filename):
            return filename

    return None


def resolve_word_clip(entry: dict) -> str | None:
    word, lang_code = entry.get('word'), entry.get('lang_code')
    if not word or not lang_code:
        return None

    if lang_code == 'en':
        committed = existing_trainer_clip(word)
        if committed:
            return committed

    titles = [commons_title(name) for name in audio_filenames_for_language(fetch_wikitext(word), lang_code)]
    urls = resolve_commons_urls(titles[:10])
    for title in titles:
        url = urls.get(title)
        if not url:
            continue
        filename = local_word_filename(word, lang_code, Path(title).suffix, slugify(entry['name']))
        if download(url, AUDIO_DIR / filename):
            return filename

    return None


def build_sections() -> list[dict]:
    resolved = []
    for entry in SYMBOLS:
        enriched = dict(entry)
        enriched['sound_audio_file'] = resolve_sound_clip(entry)
        enriched['word_audio_file'] = resolve_word_clip(entry)
        resolved.append(enriched)
        sound = enriched['sound_audio_file'] or '—'
        clip = enriched['word_audio_file'] or '—'
        print(f'  {entry["symbol"]}\t{entry["name"]}\tsound: {sound}\tword: {clip}')
        time.sleep(0.4)

    return [
        {
            'key': section['key'],
            'title': section['title'],
            'note': section['note'],
            'entries': [e for e in resolved if e['section'] == section['key']],
        }
        for section in SECTIONS
    ]


def main() -> None:
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    sections = build_sections()

    CATALOG_OUTPUT.write_text(render_csharp(sections))

    total = sum(len(s['entries']) for s in sections)
    with_sound = sum(1 for s in sections for e in s['entries'] if e['sound_audio_file'])
    with_word = sum(1 for s in sections for e in s['entries'] if e['word_audio_file'])
    print(f'\nWrote {total} symbols in {len(sections)} sections to {CATALOG_OUTPUT}')
    print(f'  sound recordings: {with_sound}/{total}')
    print(f'  example-word recordings: {with_word}/{total}')

    silent = [e['symbol'] for s in sections for e in s['entries']
              if not e['sound_audio_file'] and not e['word_audio_file']]
    if silent:
        print(f'WARNING: no recording of any kind for: {" ".join(silent)}')


if __name__ == '__main__':
    main()
