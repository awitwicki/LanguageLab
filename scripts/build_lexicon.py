"""Builds the English lexicon — every word form mapped to its lemmas, primary first — that the
server's book import and the SPA's reader both lemmatize with.

Output (generated; never edit it by hand — fix a lemma in scripts/lexicon-overrides.txt and rerun):
  * LanguageLab.Domain/Lexicon/english-lexicon.txt  (embedded in LanguageLab.Domain)
  * web/public/lexicon/english-lexicon.txt          (served to the SPA; byte-identical)
  * LICENSE-SCOWL.txt and LICENSE-AGID.txt next to both, copied verbatim.

One line per form. A lone word is a lemma of itself only; otherwise the form, then its lemmas:
    find
    found find found
    went go

Sources, both permissively licensed (attribution only):
  * SCOWL 2020.12.07 — which words exist: the english, american and british `words` lists up to
    --size (default 60); no proper names, abbreviations or contractions;
  * AGID's infl.txt from github.com/en-wl/wordlist at a pinned commit — their inflected forms.

Run with: uv run scripts/build_lexicon.py [--size 60]
The first run needs network access (downloads.sourceforge.net, raw.githubusercontent.com). The
downloads are cached in scripts/.cache/ (gitignored) and checked against the SHA-256 constants
below; a mismatch aborts the build.
"""

import argparse
import hashlib
import re
import shutil
import tarfile
import urllib.request
from collections.abc import Iterable
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CACHE = REPO / 'scripts' / '.cache'
OVERRIDES = REPO / 'scripts' / 'lexicon-overrides.txt'
OUTPUT_DIRS = (REPO / 'LanguageLab.Domain' / 'Lexicon', REPO / 'web' / 'public' / 'lexicon')
DATA_NAME = 'english-lexicon.txt'

SCOWL_VERSION = '2020.12.07'
SCOWL_URL = f'https://downloads.sourceforge.net/wordlist/scowl-{SCOWL_VERSION}.tar.gz'
SCOWL_SHA256 = '5587667caa20c4891390c2d42dbb4d5c4c3f41bee77af1457ece3ba23fb859cc'
# The directory the archive unpacks into, and its copyright notice (copied as LICENSE-SCOWL.txt).
SCOWL_ROOT = f'scowl-{SCOWL_VERSION}'
SCOWL_NOTICE = 'Copyright'
SCOWL_SIZES = (10, 20, 35, 40, 50, 55, 60, 70, 80, 95)
# `english` holds the words spelled the same everywhere; `american` and `british` hold only the
# spellings particular to each (color, colour). Only the `words` category: no proper names,
# `upper`, abbreviations or contractions.
SCOWL_SPELLINGS = ('english', 'american', 'british')

# The latest commit touching agid/ when the lexicon was first built (2024-07-22). agid/ lives on
# the en-wl/wordlist repo's `v1` branch, not its current default branch (`v2`, the in-progress
# ESDB/SCOWLv2 line) — the commit sha below is what's pinned, so the branch this was found on
# doesn't matter to the fetch itself.
AGID_COMMIT = 'b22230cc5250887737fdefe9ca4c9d9d01230eaa'
AGID_URL = 'https://raw.githubusercontent.com/en-wl/wordlist/{commit}/agid/{name}'
AGID_FILES = {
    'infl.txt': 'df0c8f6edd4193d823abfad9e4ff6f42b4526211516649e1c73d4533aef749fa',
    'README': '57cadafd8f11481454a5e269a04ffde7951cc72845643613ca4235a84b10db32',
}
# The file whose text is AGID's copyright notice, copied verbatim as LICENSE-AGID.txt.
AGID_NOTICE = 'README'

WORD = re.compile(r'[a-z]+')
ANNOTATION = re.compile(r'\{[^}]*\}')
# A variant level is SPACE-separated from the word and may be a decimal (`dreamt 1`, `waked 0.1`);
# only the whole-number part (group 1) is compared against the drop threshold.
LEVEL = re.compile(r'\s+(\d+)(?:\.\d+)?$')


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, 'rb') as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b''):
            digest.update(chunk)
    return digest.hexdigest()


def fetch(url: str, target: Path, sha256: str) -> None:
    """Downloads url to target, unless a copy with the pinned checksum is already there."""
    if target.exists() and sha256_of(target) == sha256:
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + '.part')
    request = urllib.request.Request(url, headers={'User-Agent': 'LanguageLab build_lexicon.py'})
    with urllib.request.urlopen(request, timeout=300) as response, open(partial, 'wb') as out:
        shutil.copyfileobj(response, out)
    actual = sha256_of(partial)
    if actual != sha256:
        partial.unlink()
        raise SystemExit(f'{url}: SHA-256 is {actual}, the script pins {sha256}. Refusing to build.')
    partial.replace(target)


def fetch_scowl() -> Path:
    archive = CACHE / f'scowl-{SCOWL_VERSION}.tar.gz'
    fetch(SCOWL_URL, archive, SCOWL_SHA256)
    root = CACHE / SCOWL_ROOT
    stamp = root / '.extracted'
    if not stamp.exists():
        with tarfile.open(archive, 'r:gz') as tar:
            tar.extractall(CACHE, filter='data')
        stamp.touch()
    return root


def fetch_agid() -> Path:
    directory = CACHE / f'agid-{AGID_COMMIT}'
    for name, sha256 in AGID_FILES.items():
        fetch(AGID_URL.format(commit=AGID_COMMIT, name=name), directory / name, sha256)
    return directory


def read_scowl_lemmas(final_dir: Path, max_size: int) -> tuple[dict[str, int], int]:
    """Every lowercase ASCII word of the lists up to max_size, mapped to the smallest size that
    lists it; and how many lists were read. The `english` list of each size must exist."""
    sizes: dict[str, int] = {}
    lists_read = 0
    for size in (s for s in SCOWL_SIZES if s <= max_size):
        for spelling in SCOWL_SPELLINGS:
            path = final_dir / f'{spelling}-words.{size}'
            if not path.exists():
                if spelling == 'english':
                    raise ValueError(f'{path} is missing — is the SCOWL archive complete?')
                continue
            lists_read += 1
            for line in path.read_text(encoding='latin-1').splitlines():
                word = line.strip()
                if WORD.fullmatch(word) and word not in sizes:
                    sizes[word] = size
    return sizes, lists_read


def parse_form(token: str) -> str | None:
    """One variant from an infl.txt group, or None when it is not to be kept.

    An individual entry is `<word><tags>[ <level>][ {<explanation>}]` (AGID's own grammar, its
    README) — the level, when present, is space-separated from the word and may be a decimal
    (`dreamt 1`, `waked 0.1`); only the whole-number part is compared against the drop threshold.
    Dropped: `~` (low confidence), `?` (not in the word list), `!` (per the README, likely an
    inflection of a *different*, similar word — not this lemma), a level whose whole-number part
    is >= 2. Kept and stripped: `<`, a level of 0 or 1. {Annotations} are removed first. What is
    left must be lowercase ASCII letters."""
    token = ANNOTATION.sub('', token).strip()

    level = LEVEL.search(token)
    if level:
        if int(level.group(1)) >= 2:
            return None
        token = token[:level.start()]

    if '~' in token or '?' in token or '!' in token:
        return None

    token = token.replace('<', '')
    return token if WORD.fullmatch(token) else None


def parse_infl_line(line: str) -> tuple[str, list[str]] | None:
    """`<lemma> <POS>[?]: <group> | <group> …` → (lemma, its kept forms in file order), or None
    when the line is not an entry for a lowercase single-word lemma. The field order inside a
    line does not matter: every group is just "a form of this lemma"."""
    head, colon, body = line.partition(':')
    if not colon:
        return None
    parts = head.split()
    if len(parts) != 2 or not WORD.fullmatch(parts[0]):
        return None
    lemma = parts[0]
    forms: list[str] = []
    for group in body.split('|'):
        for token in group.split(','):
            form = parse_form(token)
            if form is not None and form != lemma and form not in forms:
                forms.append(form)
    return lemma, forms


def build_entries(lemma_sizes: dict[str, int], infl_lines: Iterable[str]) -> dict[str, list[str]]:
    """form → lemmas, primary first.

    A form's lemmas are the ones it inflects, ordered by (SCOWL size, then alphabetically), and
    the form itself last when it is a lemma too. SCOWL's lists carry inflected forms as well as
    lemmas, so a word counts as a lemma when it is in the lists and is either an AGID headword or
    no inflection of one: `found → find found`, but `went → go` and `cats → cat`."""
    inflection_of: dict[str, set[str]] = {}
    headwords: set[str] = set()
    for line in infl_lines:
        parsed = parse_infl_line(line)
        if parsed is None:
            continue
        lemma, forms = parsed
        if lemma not in lemma_sizes:
            continue
        headwords.add(lemma)
        for form in forms:
            inflection_of.setdefault(form, set()).add(lemma)

    def is_lemma(word: str) -> bool:
        return word in lemma_sizes and (word in headwords or word not in inflection_of)

    entries = {word: [word] for word in lemma_sizes if is_lemma(word)}
    for form, lemmas in inflection_of.items():
        ordered = sorted(lemmas, key=lambda lemma: (lemma_sizes[lemma], lemma))
        if is_lemma(form):
            ordered.append(form)
        entries[form] = ordered
    return entries


def read_overrides(lines: Iterable[str]) -> dict[str, list[str]]:
    """lexicon-overrides.txt: `form lemma lemma …`, the output's own syntax; `#` starts a comment
    line. A lone word makes the form a lemma of itself only. A form may not be overridden twice —
    that is a typo (a second line meant a different form) rather than an intentional update."""
    overrides: dict[str, list[str]] = {}
    for number, raw in enumerate(lines, start=1):
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        words = line.split()
        if not all(WORD.fullmatch(word) for word in words):
            raise ValueError(f'lexicon-overrides.txt:{number}: not lowercase ASCII words: {line!r}')
        form, lemmas = words[0], words[1:] or words[:1]
        if len(set(lemmas)) != len(lemmas):
            raise ValueError(f'lexicon-overrides.txt:{number}: a lemma is repeated: {line!r}')
        if form in overrides:
            raise ValueError(f'lexicon-overrides.txt:{number}: {form!r} is overridden twice')
        overrides[form] = lemmas
    return overrides


def apply_overrides(entries: dict[str, list[str]], overrides: dict[str, list[str]]) -> None:
    """A override corrects a computed line; it may not conjure one up — a form with no computed
    entry is a typo, not a new word (SCOWL and AGID are the only source of which words exist)."""
    for form, lemmas in overrides.items():
        if form not in entries:
            raise ValueError(f'lexicon-overrides.txt: {form!r} has no computed entry to override')
        entries[form] = list(lemmas)


def check_closed(entries: dict[str, list[str]]) -> None:
    """Every lemma named on a line must have a line of its own, and that line must list the lemma
    among its own lemmas — otherwise a lemma could resolve to something that is not itself a
    lemma (an inflected form standing in for one), and a lookup would not be idempotent."""
    missing = sorted({lemma for lemmas in entries.values() for lemma in lemmas if lemma not in entries})
    if missing:
        raise ValueError(f'Lemmas with no line of their own: {", ".join(missing[:20])}')
    not_self = sorted({lemma for lemmas in entries.values() for lemma in lemmas
                        if lemma not in entries[lemma]})
    if not_self:
        raise ValueError(f'Lemmas that do not list themselves: {", ".join(not_self[:20])}')


def render(entries: dict[str, list[str]]) -> str:
    """Sorted by form (ordinal), one `\\n`-terminated line per form, no blank line."""
    lines = []
    for form in sorted(entries):
        lemmas = entries[form]
        lines.append(form if lemmas == [form] else ' '.join([form, *lemmas]))
    return ''.join(line + '\n' for line in lines)


def write_outputs(text: str, notices: dict[str, Path], directories: Iterable[Path]) -> None:
    data = text.encode('utf-8')
    for directory in directories:
        directory.mkdir(parents=True, exist_ok=True)
        (directory / DATA_NAME).write_bytes(data)
        for name, source in notices.items():
            shutil.copyfile(source, directory / name)


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description='Build english-lexicon.txt from SCOWL and AGID.')
    parser.add_argument('--size', type=int, default=60, choices=SCOWL_SIZES,
                        help='the largest SCOWL size to take words from (default 60)')
    args = parser.parse_args(argv)

    scowl = fetch_scowl()
    agid = fetch_agid()
    lemma_sizes, lists_read = read_scowl_lemmas(scowl / 'final', args.size)
    infl_lines = (agid / 'infl.txt').read_text(encoding='latin-1').splitlines()

    entries = build_entries(lemma_sizes, infl_lines)
    overrides = read_overrides(OVERRIDES.read_text(encoding='utf-8').splitlines())
    apply_overrides(entries, overrides)
    check_closed(entries)

    text = render(entries)
    write_outputs(text,
                  {'LICENSE-SCOWL.txt': scowl / SCOWL_NOTICE, 'LICENSE-AGID.txt': agid / AGID_NOTICE},
                  OUTPUT_DIRS)

    size = len(text.encode('utf-8'))
    multi = sum(1 for lemmas in entries.values() if len(lemmas) > 1)
    print(f'SCOWL: {len(lemma_sizes)} words from {lists_read} lists up to size {args.size}.')
    print(f'Wrote {len(entries)} lines, {size} bytes ({size / 1_000_000:.2f} MB), '
          f'{multi} multi-lemma forms, {len(overrides)} overrides.')


if __name__ == '__main__':
    main()
