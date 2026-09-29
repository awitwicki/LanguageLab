# Architecture

LanguageLab is a .NET solution plus a React SPA. The dependency direction is one-way:
`Domain` knows nothing about the outside world, `Infrastructure` and `Application` build on it,
and `Api` composes them and serves the SPA.

For the rules an agent must follow while working in any of this, see [CLAUDE.md](../CLAUDE.md);
this file is the map, not the rulebook.

## Projects

### `LanguageLab.Domain/`

Entities (`Dictionary`, `WordPair`, `WordTranslation`, `KnownWord`, `UnknownWord`, `TelegramUser`,
`Training`, `TrainingEvent`) and interfaces. No dependencies on infrastructure. A word's meaning is
`WordTranslation`, one row per `(WordPairId, Language)` — see
[vocabulary-and-training.md](vocabulary-and-training.md#translation) — and `Training.Language`
records the language a session was built and renders in.

It also holds the three catalogs that live entirely in code rather than in the database —
`IrregularVerbs/IrregularVerbCatalog`, `Pronunciation/PronunciationCatalog` and the IPA chart
`Pronunciation/IpaCatalog` (see [trainers.md](trainers.md)) — plus the `Languages/LearnerLanguages`
catalog of learner languages (see [auth.md](auth.md#learner-language)), and the value types that
guard imported text (`ImportWordText`, `WordText`, `TitleText`) and the role predicate
`Entities/UserRoles.CanPublishDirectly`.

`Lexicon/` is the English lexicon: `IEnglishLexicon` and `EnglishLexicon`, which reads the
generated `english-lexicon.txt` embedded in the assembly (see
[`scripts/build_lexicon.py`](#scriptsbuild_lexiconpy) below) on first use.
`LanguageLab.Application/Lexicon`'s `AddEnglishLexicon()` registers it as a singleton.

### `LanguageLab.Infrastructure/`

EF Core `ApplicationDbContext`, the PostgreSQL provider, and the migrations.

### `LanguageLab.Application/`

The services on top of the domain:

- **Vocabulary and training** — word selection, training sessions, book import, sorting, and
  per-scope Leitner progress (`LearningProgressService`). See
  [vocabulary-and-training.md](vocabulary-and-training.md).
- **Books and chapters** — `ChapterStatsService` (the per-chapter rows of a book, also for a
  subset of it) and `StarredChapterService` (a user's starred chapters).
- **Book parsing** — `Books/`: `BookParser : IBookParser` turns fb2 or epub bytes into a `ParsedBook`
  section tree, and `BookChapters.Flatten` cuts it into chapters by the `ChapterMode` the uploader
  picked. A port of the SPA's `web/src/books/` and `web/src/fb2/chapters.ts` (see
  [reader.md](reader.md#websrcbooks)); fb2 goes through `System.Xml`, epub documents through the NuGet
  packages `AngleSharp` (1.8.2) and `AngleSharp.Xml` (1.2.0). The input is untrusted: zip limits
  (5,000 entries, 32 MB per entry, 64 MB in total), no DTDs or external entities, element nesting
  capped at 256, 30 seconds per book.
- **Server-side import** — `Import/`: `BookFileImportService` drives bytes → SHA-256 →
  `IBookParser` → `BookChapters.Flatten` → `ImportTokenizer` (clean, stop-word and number-word
  drop, lexicon lemmas, the ≥50 % English-coverage gate) → `BookImportService`, then queues
  background translation for the importer's language. The file is parsed in memory and never
  stored.
- **The home screen's recent work** — `RecentActivityService`: the last finished session per scope,
  and the scopes still being sorted. Both drop what the caller can no longer open.
- **The personal dictionary** — `PersonalDictionaryService`, the user's own word list.
- **Translation** — `Translation/`: one language model behind `ILlmClient`, in `Translation/Llm/`
  (`GeminiLlmClient` or `OpenAiCompatibleLlmClient`, picked by `Translation:Provider` through
  `LlmClientSelector`; `AddLlmClient` registers it, and `LlmStartupCheck` logs a warning when
  there is no key). On top of it: `IWordBatchTranslator` → `LlmWordBatchTranslator`,
  `ITranslator` → `LlmTranslator` (one word as a batch of one; `AddWordTranslation()`), and
  `ISentenceTranslator` → `LlmSentenceTranslator` (`AddSentenceTranslation()`).
  `TranslationService` tries the shared vocabulary first and the model after, a miss paced by
  `UncachedTranslationLimiter` (one per user every 10 seconds, shared with sentences). See
  [vocabulary-and-training.md](vocabulary-and-training.md#translation).
- **Background translation** — `Translation/Queue/`: `TranslationQueue` (`ITranslationQueue`)
  keeps one `TranslationJob` per dictionary and language, and the hosted `TranslationWorker` fills
  the missing translations through `IWordBatchTranslator`, one batch of one job per iteration
  (`TranslationJobProcessor`). `AddTranslationQueue()` registers all of it; see
  [vocabulary-and-training.md](vocabulary-and-training.md#background-translation-queue).
- **The irregular-verbs trainer** — `VerbKnowledgeService`, `VerbSessionService`.
- **The reader** — `ReaderBookService`, `ReaderWordStatusService`, `ReaderWordService`. What each
  one owns is in [reader.md](reader.md).

### `LanguageLab.Api/`

ASP.NET Core Minimal API that also serves the SPA, and runs the DB migrations on startup. `Auth/`
holds the claims, the session validation and the OIDC event handlers — see [auth.md](auth.md).
The endpoints are inventoried below.

`SpaHosting` serves the SPA. `index.html` goes out with `Cache-Control: no-cache` and the hashed
`assets/*` as immutable, so no WebView runs last release's client against this release's API. An
unknown `/api/...` path answers 404 rather than the page; everything else no file matches gets
`index.html`.

### `LanguageLab.TgBot/`

The Telegram bot: a Generic Host console app on `Telegram.Bot` (long polling), with no database
and no project references. `/start`, and in fact any private message, answers with the bot name, a
description and an **Open LanguageLab** `web_app` button; the chat menu button is set to the same
URL at startup. Configured by `Telegram:BotToken` and `WebApp:Url` (`BotOptions`); the copy and
keyboard live in `StartMessage`, the polling in `BotService`. It has its own Dockerfile and compose
service.

### `web/`

React + Vite SPA: book import (fb2, epub, zipped fb2 — the browser previews chapters, the server
does the rest), dictionary stats, word sorting.

- `src/layout/` — the shell: top bar and sidebar.
- `src/screens/`, `src/components/`.
- `src/lib/` — formatters and shared hooks, such as `useDismiss` for Escape/outside-press closing
  of menus.
- `src/reader/` and `src/books/` — the Reading mode and everything format-dependent. See
  [reader.md](reader.md).
- `src/fb2/chapters.ts` — fb2 sections and `flattenChapters`, the import screen's chapter preview
  (the server's `BookChapters.Flatten` follows the same rule); `src/fb2/tokenize.ts` — the reader's
  word tokenizer. Lemmatization is no longer done in the browser for import: `ImportScreen` uploads
  the file itself (`api.importDictionary`, over `XMLHttpRequest` for upload progress).
- `src/lexicon/lexicon.ts` — `loadLexicon()`: the English lexicon, fetched once per page load
  from `/lexicon/english-lexicon.txt` (a copy of the server's file) and parsed into
  `lemmasOf`/`lemmaOf`; the reader's highlights and word panel resolve lemmas with it.
- Tests are `*.test.ts(x)` next to the code they cover (vitest + jsdom, helper
  `src/test/render.ts`).

More detail in [web/README.md](../web/README.md).

### `extract.py`

A Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under
`dictionaries/`.

### `scripts/build_lexicon.py`

Builds the English lexicon — every word form mapped to its lemmas, primary first (`went go`,
`found find found`) — from SCOWL 2020.12.07 (the `english`, `american` and `british` word lists
up to size 60) and AGID's `infl.txt` (github.com/en-wl/wordlist at `b22230cc5250`). An inflection
wins the primary slot; the form itself comes last when it is a lemma too, which means it is in
the SCOWL lists and is an AGID headword or no inflection of one (`houses house`, but
`found find found`). Hand-kept fixes are in `scripts/lexicon-overrides.txt` — 21 entries: `lay`
plus 20 common words the final review found buried behind a rarer AGID-generated
comparative/superlative (`number`, `interest`, `morning`, …).

The output is 92823 lines, 1.37 MB (290 KB gzipped), 2891 of them forms with more
than one lemma. It is written twice and a test keeps the copies byte-identical:
`LanguageLab.Domain/Lexicon/english-lexicon.txt` (embedded, read by `EnglishLexicon`) and
`web/public/lexicon/english-lexicon.txt` (a static asset, read by `src/lexicon/lexicon.ts`).
SCOWL's and AGID's notices sit next to both as `LICENSE-SCOWL.txt` / `LICENSE-AGID.txt`.

The server-side book import lemmatizes and whitelists with it (`ImportTokenizer` in
`LanguageLab.Application/Import/`), and the reader's highlights and word panel lemmatize with the
SPA's copy (`src/lexicon/lexicon.ts`).

## API surface

### Dictionaries and sorting

- `/api/dictionaries`, `/api/sorting`.
- `GET /api/dictionaries/personal`, `POST|PUT|DELETE /api/dictionaries/personal/words[/{id}]` — the
  personal dictionary; the `PUT` corrects a word's translation and nothing else.
- `GET /api/translate?word=` — a translation suggestion; 429 with `Retry-After` for a miss inside
  the user's 10-second uncached-translation window, or past 500 lookups a day.
- `POST /api/dictionaries/import` — multipart book upload; the server parses, lemmatizes and
  imports (see [vocabulary-and-training.md](vocabulary-and-training.md#import-validation)).
- `GET /api/dictionaries/{id}/translation` — the background translation job's `{ done, total }`
  for the caller's language, read-only (the dictionary screen polls it; `GET /api/dictionaries/{id}`
  also enqueues).
- `POST|DELETE /api/dictionaries/{id}/publication` — the owner offers a dictionary for publication
  or withdraws the offer.
- `GET /api/admin/dictionaries` — the moderation queue (`status` query parameter, defaulting to
  `pending`); `POST /api/admin/dictionaries/{id}/approve|reject`.
- `DELETE /api/admin/users/{id}/dictionaries` — bulk-delete a user's dictionaries, for use
  alongside a ban.
- `GET /api/admin/shelf-words` — the shelf admin panel: the calling admin's own words (`status`,
  `search`, `page`, `pageSize` query parameters; `status` absent lists every shelf). Re-shelving a
  row reuses `POST /api/sorting/mark` — there is no separate write endpoint.

### Training

`/api/training` is a Leitner quiz on top of `TrainingSessionService`. The question queue lives in
the DB; the SPA grades each answer in the browser from the question's own `wordPairId` and posts it
in the background, prefetching the next question behind it (`TrainingScreen.tsx`).

- `GET /api/training/preview` — the scope's progress scale plus batch candidates by frequency;
  `new-batch` accepts explicit `wordPairIds`.
- `POST /api/training/review` takes an optional `{ dictionaryId, chapterIds }` body. Without it the
  review is global, as started from the home screen's "Due today" tile; with it, only that scope's
  due words — the book header reviews the book, and a chapter row reviews its chapter once it has
  no new words left.
- `GET /api/training/stats` — the user's global Leitner standing (per-box counts, learned, due),
  rendered on the home screen.

### Chapters

`/api/chapters` — `GET /starred` (the home screen's starred chapters, ordered by book name then
chapter order) and `PUT|DELETE /{id}/star` (204; 404 for a chapter of an invisible book, or when
there is nothing to unstar).

### Home

`GET /api/home/recent` — the two lists the home screen offers to pick up again, newest first: the
last finished session per scope and mode (its "Repeat" reopens that scope — a batch on its start
screen, a review by asking for what is due now), and the scopes still being sorted. A scope is a
book or one of its chapters; `Training.ChapterId` records the first, a `SortingVisit` row upserted
by `POST /api/sorting/mark`'s optional `{ dictionaryId, chapterId }` the second — the shelves alone
cannot say where the user was, since one word sits in several books.

The same card carries a third row, **Reading**, which has no endpoint of its own: `GET
/api/reader/books` already comes back newest-first, and `useContinueReading` keeps the newest book
whose file this device actually holds. See [reader.md](reader.md).

### Auth and admin

`/api/auth/*` — `telegram/start`, the handler-owned `telegram/callback`, `telegram/webapp`, `me`,
`PUT /me/language`, `logout`. `/api/admin/users*` — list, ban, unban, role, delete. See
[auth.md](auth.md#learner-language) for the language pick.

### Languages

`GET /api/languages` — the `LearnerLanguages` catalog (`code`, `englishName`, `nativeName`), in
catalog order; anonymous, since the picker may render before `/me` settles. See
[auth.md](auth.md#learner-language).

### Reader

`/api/reader`:

- `GET /capabilities` — `sentenceTranslation` (false when no language model is configured) and
  `importRetryAfterSeconds` (the wait before the user's next import, or null).
- `GET /books` — the reader's library.
- `PUT /books/{hash}` — register or refresh a book by its file hash, idempotent.
- `DELETE /books/{hash}`, `PUT /books/{hash}/position`.
- `GET /word-statuses`.
- `GET /words/{lemma}?dictionaryId=` — the word panel's lookup; its `shelf` and `canReset` drive
  the buttons.
- `POST /words/{lemma}/learn`, `POST /words/{lemma}/known`, `POST /words/{lemma}/ignore`.
- `DELETE /words/{lemma}/shelf` — the panel's undo: 204, or 409 when the word gained a Leitner row
  since the panel loaded it.

`POST /api/translate/sentence` — the reader's sentence translation through the language model;
400 over 500 characters, 429 with `Retry-After` inside the user's 10-second window, 404 when no
model is configured. Nothing is stored. See [reader.md](reader.md#sentence-translation).

### Trainers

`/api/irregular-verbs` and `/api/pronunciation` — see [trainers.md](trainers.md).

## Configuration and database

Postgres runs via `compose.yaml`. `LanguageLab.Api` reads `ConnectionStrings:DefaultConnection`,
`Telegram:ClientId` and `Telegram:ClientSecret` from
`appsettings.json`/`appsettings.Development.json` — the latter is gitignored and local only. In
Docker the same keys arrive as environment variables through the ASP.NET Core `__` convention.
`WebUser:TelegramId` is gone; there is no config user any more.

`Translation:Provider` (`Gemini` | `OpenAiCompatible`), `Translation:Gemini:ApiKey|Model` and
`Translation:OpenAi:BaseUrl|ApiKey|Model` pick and reach the language model (`LlmOptions`; the
full list is in the README). Without the selected provider's key the app still starts and
translation is off — see [vocabulary-and-training.md](vocabulary-and-training.md#translation).

Migrations run automatically on startup, via `dbContext.Database.MigrateAsync()` in
[Program.cs:39](../LanguageLab.Api/Program.cs#L39). To add one:

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.Api migrations add <Name>
```
