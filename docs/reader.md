# The reader

Reading mode lets a learner read a book and act on its words in place. The defining constraint:
**the book file stays in the browser and is never uploaded.**

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

Opening a book in the reader outside Telegram builds its dictionary in the background for any
signed-in user: private, named after the book, leaf chapters, bounded by the same 20-imports-a-day
limit as a manual import. The import screen also puts an imported book in the reader.

## Server-side services

- **`ReaderBookService`** — the reader's library: file hash, title, author, position, and the
  dictionary the same file was imported as, if any.
- **`ReaderWordStatusService`** — a word's standing, derived from the same Leitner, shelf and
  personal rows training uses. `GetAsync` gives the New/Learning/Known that the highlights use;
  `GetShelfAsync` gives the panel's New/Learning/Known/**Ignored**, plus whether a Leitner row makes
  it a word really in training.
- **`ReaderWordService`** — the word panel's lookup and its three buttons:
  - **Add to training** shelves the word in the dictionary of the book being read when the word is
    there, and otherwise adds it to "My words" (`LearnTargetAsync`).
  - **I know it** and **Ignore** shelve the shared row — know, or exclude — creating it untranslated
    when it is missing.
  - `ResetAsync` is the undo behind the marked button: off every shelf and out of "My words". It is
    refused for a word that has a `WordProgress` row.

The three buttons are one picker: the word's current shelf is marked (`aria-pressed`), another
button moves it there, and tapping the marked one undoes it. The endpoints are listed in
[architecture.md](architecture.md#reader).

## `web/src/reader/`

- **`readerBook.ts`** — the reader's own parser, separate from the import's: sentence and word
  tokenizing, fb2 and epub through `books/`. A chapter past `MAX_CHAPTER_SENTENCES` (800, above any
  real chapter) is cut into parts by `splitLongChapters`, so a book with no structure of its own is
  not one chapter from cover to cover. `clampPosition` carries a paragraph past the end of its
  chapter into the chapters after it, keeping the place of a position stored before the cut.
- **`bookStore.ts`** — local storage of the book files themselves, in IndexedDB, never uploaded.
- **`readerSettings.ts`** — theme, dimming, font size.
- **`ReaderLibraryScreen.tsx`** — the library screen.
- **`ReaderScreen.tsx`, `Sentence.tsx`, `WordPanel.tsx`, `ReaderMenu.tsx`** — the full-screen
  reader.
- **`wordStatus.ts`** — word-status resolution.
- **`chapterWindow.ts`** — the windowed rendering of a chapter, in chunks of `CHUNK_SENTENCES`.
  `ReaderScreen` keeps the chunks around the place being read in the DOM and leaves the rest as
  `.reader-gap` divs of estimated height, each one an IntersectionObserver target that mounts its
  own chunk before it reaches the screen, holding the sentence being read still when a chunk above
  it mounts. Where there is no IntersectionObserver, the whole chapter renders as it did before.
- **`useReaderPosition.ts`, `useSentenceTranslations.ts`** — position sync with the server.
- **`useAutoImport.ts`** — opening a book with no dictionary builds one in the background.

## `web/src/books/`

Everything format-dependent, and the only place that branches on format:

- **`zip.ts`** — fflate.
- **`decode.ts`** — `decodeXml`, honouring the declared encoding, since fb2 books are often
  windows-1251.
- **`epub.ts`** — container → OPF → spine; one XHTML document per chapter, titles from the book's
  own TOC.
- **`formatError.ts`** — `BookFormatError`: `invalid` or `encrypted` (DRM).
- **`format.ts`** — `readBookSource`: the bytes decide the format, never the extension.
- **`toParsedBook.ts`** — produces the import's `ParsedBook`. The reader's own shape is built in
  `reader/readerBook.ts` instead.

## Sentence translation

`POST /api/translate/sentence` uses DeepL when `Translation:DeepLApiKey` is set and MyMemory
otherwise, stores nothing, and answers 413 for a sentence MyMemory cannot take (over 500 bytes) or
429 past the user's 20 000-characters-a-day quota (`SentenceQuota`). The server-wide budget behind
it is in [vocabulary-and-training.md](vocabulary-and-training.md#budgets).
