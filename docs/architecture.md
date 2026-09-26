# Architecture

LanguageLab is a .NET solution plus a React SPA. The dependency direction is one-way:
`Domain` knows nothing about the outside world, `Infrastructure` and `Application` build on it,
and `Api` composes them and serves the SPA.

For the rules an agent must follow while working in any of this, see [CLAUDE.md](../CLAUDE.md);
this file is the map, not the rulebook.

## Projects

### `LanguageLab.Domain/`

Entities (`Dictionary`, `WordPair`, `KnownWord`, `UnknownWord`, `TelegramUser`, `Training`,
`TrainingEvent`) and interfaces. No dependencies on infrastructure.

It also holds the three catalogs that live entirely in code rather than in the database —
`IrregularVerbs/IrregularVerbCatalog`, `Pronunciation/PronunciationCatalog` and the IPA chart
`Pronunciation/IpaCatalog` (see [trainers.md](trainers.md)) — and the value types that guard imported text
(`ImportWordText`, `WordText`, `TitleText`) and the role predicate
`Entities/UserRoles.CanPublishDirectly`.

### `LanguageLab.Infrastructure/`

EF Core `ApplicationDbContext`, the PostgreSQL provider, and the migrations.

### `LanguageLab.Application/`

The services on top of the domain:

- **Vocabulary and training** — word selection, training sessions, book import, sorting, and
  per-scope Leitner progress (`LearningProgressService`). See
  [vocabulary-and-training.md](vocabulary-and-training.md).
- **Books and chapters** — `ChapterStatsService` (the per-chapter rows of a book, also for a
  subset of it) and `StarredChapterService` (a user's starred chapters).
- **The home screen's recent work** — `RecentActivityService`: the last finished session per scope,
  and the scopes still being sorted. Both drop what the caller can no longer open.
- **The personal dictionary** — `PersonalDictionaryService`, the user's own word list.
- **Translation** — `Translation/`: `ITranslator` → `MyMemoryTranslator`, with
  `TranslationService` trying the shared vocabulary first and the provider after;
  `ISentenceTranslator` → `FallbackSentenceTranslator`, which uses DeepL when a key is set and
  `MyMemorySentenceTranslator` otherwise.
- **The irregular-verbs trainer** — `VerbKnowledgeService`, `VerbDrillService`.
- **The reader** — `ReaderBookService`, `ReaderWordStatusService`, `ReaderWordService`. What each
  one owns is in [reader.md](reader.md).

### `LanguageLab.Api/`

ASP.NET Core Minimal API that also serves the SPA, and runs the DB migrations on startup. `Auth/`
holds the claims, the session validation and the OIDC event handlers — see [auth.md](auth.md).
The endpoints are inventoried below.

### `LanguageLab.TgBot/`

The Telegram bot: a Generic Host console app on `Telegram.Bot` (long polling), with no database
and no project references. `/start`, and in fact any private message, answers with the bot name, a
description and an **Open LanguageLab** `web_app` button; the chat menu button is set to the same
URL at startup. Configured by `Telegram:BotToken` and `WebApp:Url` (`BotOptions`); the copy and
keyboard live in `StartMessage`, the polling in `BotService`. It has its own Dockerfile and compose
service.

### `web/`

React + Vite SPA: book import in the browser (fb2, epub, zipped fb2), dictionary stats, word
sorting.

- `src/layout/` — the shell: top bar and sidebar.
- `src/screens/`, `src/components/`.
- `src/lib/` — formatters and shared hooks, such as `useDismiss` for Escape/outside-press closing
  of menus.
- `src/reader/` and `src/books/` — the Reading mode and everything format-dependent. See
  [reader.md](reader.md).
- `src/fb2/wordExtractor.ts` — the word-extraction worker behind a promise, shared by the import
  screen and the reader.
- Tests are `*.test.ts(x)` next to the code they cover (vitest + jsdom, helper
  `src/test/render.ts`).

More detail in [web/README.md](../web/README.md).

### `extract.py`

A Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under
`dictionaries/`.

## API surface

### Dictionaries and sorting

- `/api/dictionaries`, `/api/sorting`.
- `GET /api/dictionaries/personal`, `POST|DELETE /api/dictionaries/personal/words[/{id}]` — the
  personal dictionary.
- `GET /api/translate?word=` — a translation suggestion.
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
`logout`. `/api/admin/users*` — list, ban, unban, role, delete. See [auth.md](auth.md).

### Reader

`/api/reader`:

- `GET /capabilities` — `sentenceTranslation`, always true, because `FallbackSentenceTranslator`
  always has MyMemory to fall back on.
- `GET /books` — the reader's library.
- `PUT /books/{hash}` — register or refresh a book by its file hash, idempotent.
- `DELETE /books/{hash}`, `PUT /books/{hash}/position`.
- `GET /word-statuses`.
- `GET /words/{lemma}?dictionaryId=` — the word panel's lookup; its `shelf` and `canReset` drive
  the buttons.
- `POST /words/{lemma}/learn`, `POST /words/{lemma}/known`, `POST /words/{lemma}/ignore`.
- `DELETE /words/{lemma}/shelf` — the panel's undo: 204, or 409 when the word gained a Leitner row
  since the panel loaded it.

`POST /api/translate/sentence` — the reader's sentence translation (DeepL when
`Translation:DeepLApiKey` is set, MyMemory otherwise); 413 for a sentence MyMemory cannot take
(over 500 bytes), 429 past the user's 20 000-characters-a-day quota (`SentenceQuota`). Nothing is
stored.

### Trainers

`/api/irregular-verbs` and `/api/pronunciation` — see [trainers.md](trainers.md).

## Configuration and database

Postgres runs via `compose.yaml`. `LanguageLab.Api` reads `ConnectionStrings:DefaultConnection`,
`Telegram:ClientId` and `Telegram:ClientSecret` from
`appsettings.json`/`appsettings.Development.json` — the latter is gitignored and local only. In
Docker the same keys arrive as environment variables through the ASP.NET Core `__` convention.
`WebUser:TelegramId` is gone; there is no config user any more.

`Translation:MyMemoryEmail` and `Translation:DeepLApiKey` are optional; what changes without them
is described in [vocabulary-and-training.md](vocabulary-and-training.md).

Migrations run automatically on startup, via `dbContext.Database.MigrateAsync()` in
[Program.cs:39](../LanguageLab.Api/Program.cs#L39). To add one:

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.Api migrations add <Name>
```
