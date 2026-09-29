# The reader

Reading mode lets a learner read a book and act on its words in place. The defining constraint:
**the book file stays in the browser; it is sent once, for import, and never stored on the
server.**

## What the server keeps

Only a `ReaderBook` row: the file's SHA-256 (`ReaderHash`), title, author, chapter count, and the
reading position — chapter, paragraph and sentence index, plus a 0..1 progress.

On a position update from any device the `clientUpdatedAt` timestamp decides and the later one
wins, capped at the server's own clock.

`Dictionary.FileHash` is set on import, and is also a SHA-256 of the book file, so the reader can
point a book at the dictionary it was imported as. Dictionaries imported before that feature have
none, so `ReaderBookService` falls back to a visible, non-personal dictionary whose `Name` equals
the book's title — a hash match always wins over this name match. Either way, opening or importing
such a book links the existing dictionary instead of duplicating it.

Opening a book never imports it on its own. The client hashes the file and links a visible
dictionary with that hash — a client-side hash is safe for a read-only link. With none, the reader
offers a **Build dictionary** button: it uploads the file through `POST /api/dictionaries/import`
(private, named after the book, leaf chapters, with upload progress) and spends the day's one
import like the import screen does. Once the import is spent, the button is disabled and says when
it frees up (`importRetryAfterSeconds` from `GET /api/reader/capabilities`). The import screen also
puts an imported book in the reader.

Opening a book linked to a dictionary queues that dictionary's missing translations in the
learner's language (`PUT /api/reader/books/{hash}`, see
[vocabulary-and-training.md](vocabulary-and-training.md#background-translation-queue)).

## Server-side services

- **`ReaderBookService`** — the reader's library: file hash, title, author, position, and the
  dictionary the same file was imported as, if any.
- **`ReaderWordStatusService`** — a word's standing, derived from the same Leitner, shelf and
  personal rows training uses. `GetAsync` gives the New/Learning/Known that the highlights use;
  `GetShelfAsync` gives the panel's New/Learning/Known/**Ignored**, plus whether a Leitner row makes
  it a word really in training.
- **`ReaderWordService`** — the word panel's lookup and its three buttons, translated into the
  learner's language (`WordPanel.tsx` and `Sentence.tsx` render with `lang={language}` rather than
  a hardcoded `"uk"`):
  - **Add to training** shelves the word in the dictionary of the book being read when the word is
    there, and otherwise adds it to "My words" (`LearnTargetAsync`).
  - **I know it** and **Ignore** shelve the shared row — know, or exclude — creating it untranslated
    when it is missing.
  - `ResetAsync` is the undo behind the marked button: off every shelf and out of "My words". It is
    refused for a word that has a `WordProgress` row.
  - The lookup translates through `TranslationService.LookupAsync`. A miss inside the user's
    10-second uncached-translation window still answers 200, with `source: "rateLimited"` and
    `retryAfterSeconds`: the panel keeps its buttons and says when to tap the word again.

The three buttons are one picker: the word's current shelf is marked (`aria-pressed`), another
button moves it there, and tapping the marked one undoes it. The endpoints are listed in
[architecture.md](architecture.md#reader).

## `web/src/reader/`

- **`readerBook.ts`** — the reader's own parser, separate from the import's: sentence and word
  tokenizing, fb2 and epub through `books/`. A chapter past `MAX_CHAPTER_SENTENCES` (800, above any
  real chapter) is cut into parts by `splitLongChapters`, so a book with no structure of its own is
  not one chapter from cover to cover. `clampPosition` carries a paragraph past the end of its
  chapter into the chapters after it, keeping the place of a position stored before the cut.
- **`bookStore.ts`** — local storage of the book files themselves, in IndexedDB; the file leaves
  the device only when the learner builds its dictionary.
- **`wordStatus.ts`** — word-status resolution. `resolveWord` and `bookLemmaCounts` lemmatize
  through the shared English lexicon (`src/lexicon/lexicon.ts`, the same table the server's import
  uses — context-free, so an ambiguous form gets the one primary lemma the table records). The
  lexicon arrives as `Lexicon | null`: while it loads, or if it fails to, highlights degrade like a
  failed word-status fetch ("Word highlights unavailable") and reading is never blocked.
- **`readerSettings.ts`** — theme, dimming, font size.
- **`ReaderLibraryScreen.tsx`** — the library screen.
- **`useContinueReading.ts`** — the home screen's "Continue" row: the newest book of the server's
  library whose file is in this device's `bookStore`. Both halves are needed, since the server
  knows every book's position but not which files this browser holds — a book read elsewhere is
  left out rather than offered, because opening it here needs the file picker the library has.
- **`ReaderScreen.tsx`, `Sentence.tsx`, `WordPanel.tsx`, `ReaderMenu.tsx`** — the full-screen
  reader.
- **`chapterWindow.ts`** — the windowed rendering of a chapter, in chunks of `CHUNK_SENTENCES`.
  `ReaderScreen` keeps the chunks around the place being read in the DOM and leaves the rest as
  `.reader-gap` divs of estimated height, each one an IntersectionObserver target that mounts its
  own chunk before it reaches the screen, holding the sentence being read still when a chunk above
  it mounts. Two things put the whole chapter in the DOM instead: the menu's **Whole chapter**
  setting (`readerSettings.wholeChapter`), the escape hatch for find-in-page and screen readers,
  which reach only what is in the page; and a browser with no IntersectionObserver, which has
  nothing to grow a window with. Turning the setting either way rebuilds the window around the
  sentence being read, not around the last jump, so the reader's own surroundings are never
  unmounted under them.
- **`useReaderPosition.ts`** — position sync with the server.
- **`useSentenceTranslations.ts`** — sentence translation, cached on the device (see below); a
  429 becomes "Too many translations at once. Try again in …", using the wait from `Retry-After`.
- **`openBook.ts`** — a chosen file → hashed, parsed, kept in `bookStore`.

## `web/src/books/`

Everything format-dependent, and the only place that branches on format:

- **`zip.ts`** — fflate.
- **`decode.ts`** — `decodeXml`, honouring the declared encoding, since fb2 books are often
  windows-1251.
- **`epub.ts`** — container → OPF → spine; one XHTML document per chapter, titles from the book's
  own TOC. A TOC entry pointing inside a document (`ch.xhtml#id`) cuts it into a chapter of its own
  where that id sits, so a book shipped as one document still gets the chapters its TOC lists.
- **`formatError.ts`** — `BookFormatError`: `invalid` or `encrypted` (DRM).
- **`format.ts`** — `readBookSource`: the bytes decide the format, never the extension.
- **`toParsedBook.ts`** — produces the import's `ParsedBook`. The reader's own shape is built in
  `reader/readerBook.ts` instead.

The server has a .NET twin of these parsers in `LanguageLab.Application/Books/` — `BookParser`
(`format.ts` and `toParsedBook.ts`), `SafeZip`, `XmlDecoding`, `Fb2Parser` (`fb2/chapters.ts`'s
`parseBook`), the epub files (`epub.ts`) and `BookChapters.Flatten` (`flattenChapters`) — which the
import uses. Each .NET file names the TS it ports. A change to a parsing rule (what counts as a
paragraph, where a chapter is cut, where a title comes from) is made on both sides, together with the
ported tests in `LanguageLab.Tests/Books/`.

## Sentence translation

`POST /api/translate/sentence` translates one sentence into the learner's language through the
language model (`LlmSentenceTranslator`), with a hardened prompt: a fixed system instruction that
names the sentence as data to translate, never as instructions; the sentence sent only as the user
message; the answer read only from a `{"translation": "..."}` JSON schema; no secrets or tools in
the model's context. Nothing is stored or logged on the server — there is no server-side sentence
cache, so every request reaches the model and spends the user's
[one uncached translation per 10 seconds](vocabulary-and-training.md#uncached-translation-limit).

It answers 404 when no language model is configured (`GET /api/reader/capabilities` then reports
`sentenceTranslation: false` and the reader offers no translation), 400 for empty text or text over
500 characters, 429 with `Retry-After` inside the 10-second window, 503 `{ reason: "quota" }` when
the provider's quota is gone, and 502 for any other provider failure.

The device-side cache in `bookStore.ts` keys each translation `${hash}:${language}:${key}`, so
switching the learner's language never serves back another language's cached text.
