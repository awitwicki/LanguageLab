# LanguageLab

Web app for learning new words from books. Users pick a dictionary extracted from a book, then learn words through procedural exercises in the SPA.

## Rules for Claude agents

- **Never commit or push** until the user explicitly asks. Show the diff, wait for approval.
- Do not run `git add`, `git commit`, `git push`, or any history-rewriting command on your own.
- When the user has allowed committing step-by-step during multi-step work (e.g. executing a plan — one commit per task), once all steps are done, squash those commits into one (`git reset --soft` to the state before the first of them, then commit again as a single commit) **before considering the work done**. The user reviews one commit before push, not a series. This is squashing intermediate commits within already-approved work, not standalone permission for a new commit — but the push itself still waits for a separate request.
- Creating migrations is fine (`dotnet ef ... migrations add`), but `--startup-project` is now `LanguageLab.Api`.
- Everything in the project — code, comments, doc-comments, commit messages, documentation — is English. The one exception: the vocabulary translation shown to the learner (`WordPair.Translation`) stays Ukrainian, since teaching Ukrainian-speaking learners English is the whole point of the app. UI copy in the SPA is also English now (see "Frontend conventions" below).

## Project layout

- `LanguageLab.Domain/` — entities (`Dictionary`, `WordPair`, `KnownWord`, `UnknownWord`, `TelegramUser`, `Training`, `TrainingEvent`) and interfaces. No dependencies on infrastructure.
- `LanguageLab.Infrastructure/` — EF Core `ApplicationDbContext`, PostgreSQL provider, migrations.
- `LanguageLab.Application/` — services on top of the domain: word selection, training sessions, book import, sorting, per-scope Leitner progress (`LearningProgressService`), the irregular-verbs trainer (`VerbProgressService`, `VerbSessionService`); `ChapterStatsService` (the per-chapter rows of a book, also for a subset), `StarredChapterService` (a user's starred chapters); `PersonalDictionaryService` (the user's own word list) and `Translation/` (`ITranslator` → `MyMemoryTranslator`, `TranslationService` — shared vocabulary first, provider after; `ISentenceTranslator` → `FallbackSentenceTranslator` (DeepL when a key is set, `MyMemorySentenceTranslator` otherwise)). The reader's own services: `ReaderBookService` (the reader's library — file hash, title, author, position, and the dictionary the same file was imported as, if any), `ReaderWordStatusService` (a word's standing, derived from the same Leitner/shelf/personal rows training uses — `GetAsync` gives the New/Learning/Known the highlights use, `GetShelfAsync` the panel's New/Learning/Known/**Ignored** plus whether a Leitner row makes it a word really in training) and `ReaderWordService` (the word panel's lookup and its three buttons: Add to training shelves the word in the dictionary of the book being read when it is there, otherwise adds it to "My words"; I know it and Ignore shelve the shared row — know / exclude — creating it untranslated when missing; `ResetAsync` is the undo behind the marked button — off every shelf and out of "My words", refused for a word with a `WordProgress` row). Used by the API.
- `LanguageLab.Api/` — ASP.NET Core Minimal API + serves the SPA. Runs DB migrations. Endpoints: `/api/dictionaries`, `/api/sorting`, `/api/training` (Leitner quiz on top of `TrainingSessionService`; the question queue lives in the DB; the SPA grades each answer in the browser from the question's own `wordPairId` and posts it in the background, prefetching the next question behind it — `TrainingScreen.tsx`); `GET /api/training/preview` — scope's progress scale + batch candidates by frequency, `new-batch` accepts explicit `wordPairIds`.
  `POST /api/training/review` takes an optional `{ dictionaryId, chapterIds }` body: without it the review is global (started from the home screen's "Due today" tile), with it only that scope's due words — the book header reviews the book, a chapter row its chapter once it has no new words left.
  `GET /api/training/stats` — the user's global Leitner standing (per-box counts, learned, due), rendered on the home screen.
  `/api/chapters` — `GET /starred` (the home screen's starred chapters, ordered by book name then chapter order), `PUT|DELETE /{id}/star` (204; 404 for a chapter of an invisible book, or nothing to unstar).
  `Auth/` (claims, session validation, the OIDC event handlers), `/api/auth/*`
  (telegram/start, the handler-owned telegram/callback, telegram/webapp, me, logout), `/api/admin/users*`
  (list, ban, unban, role, delete);
  `GET /api/dictionaries/personal`, `POST|DELETE /api/dictionaries/personal/words[/{id}]` — the personal dictionary; `GET /api/translate?word=` — a translation suggestion. `POST|DELETE /api/dictionaries/{id}/publication` — the owner offers a dictionary for publication or withdraws the offer; `GET /api/admin/dictionaries` — the moderation queue (`status` query param, defaults to `pending`), `POST /api/admin/dictionaries/{id}/approve|reject`, `DELETE /api/admin/users/{id}/dictionaries` — bulk-delete a user's dictionaries, for use alongside a ban.
  `/api/reader` — `GET /capabilities` (`sentenceTranslation`, always true — `FallbackSentenceTranslator` always has MyMemory to fall back on), `GET /books` (the reader's library), `PUT /books/{hash}` (register/refresh a book by its file hash, idempotent), `DELETE /books/{hash}`, `PUT /books/{hash}/position`, `GET /word-statuses`, `GET /words/{lemma}?dictionaryId=` (the word panel's lookup — its `shelf` and `canReset` drive the buttons), `POST /words/{lemma}/learn`, `POST /words/{lemma}/known`, `POST /words/{lemma}/ignore`, `DELETE /words/{lemma}/shelf` (the panel's undo: 204, or 409 when the word gained a Leitner row since the panel loaded it). The three buttons are one picker — the word's shelf is marked (`aria-pressed`), another button moves it there, the marked one tapped again undoes it. `POST /api/translate/sentence` — the reader's sentence translation (DeepL when `Translation:DeepLApiKey` is set, MyMemory otherwise), 413 for a sentence MyMemory cannot take (over 500 bytes), 429 past the user's 20 000-characters-a-day quota (`SentenceQuota`), nothing stored.
- `LanguageLab.TgBot/` — the Telegram bot: a Generic Host console app on `Telegram.Bot` (long polling), no database, no project references. `/start` (any private message) answers with the bot name, a description and an **Open LanguageLab** `web_app` button; the chat menu button is set to the same URL at startup. Config `Telegram:BotToken`, `WebApp:Url` (`BotOptions`); copy and keyboard in `StartMessage`; polling in `BotService`. Own Dockerfile and compose service.
- `web/` — React + Vite SPA: book import in the browser (fb2, epub, zipped fb2), dictionary stats, word sorting. `src/layout/` (shell: top bar + sidebar), `src/screens/`, `src/components/`, `src/lib/` (formatters, shared hooks such as `useDismiss` — Escape/outside-press closing for menus), tests `*.test.ts(x)` next to the code (vitest + jsdom, helper `src/test/render.ts`). Details in [web/README.md](web/README.md).
  `src/reader/` — the Reading mode: its own reader-side parser (`readerBook.ts`, sentence/word tokenizing, fb2 and epub through `books/`; a chapter past `MAX_CHAPTER_SENTENCES` — 800, above any real chapter — is cut into parts by `splitLongChapters`, so a book with no structure of its own is not one chapter from cover to cover, and `clampPosition` carries a paragraph past the end of its chapter into the chapters after it, keeping the place of a position stored before the cut) and local storage (`bookStore.ts`, the book files themselves, IndexedDB — never uploaded — and `readerSettings.ts` for theme/dimming/font size), the library screen (`ReaderLibraryScreen.tsx`) and the full-screen reader (`ReaderScreen.tsx`, `Sentence.tsx`, `WordPanel.tsx`, `ReaderMenu.tsx`), word-status resolution (`wordStatus.ts`), the windowed rendering of a chapter (`chapterWindow.ts` — a chapter's sentences in chunks of `CHUNK_SENTENCES`; `ReaderScreen` keeps the chunks around the place being read in the DOM and leaves the rest as `.reader-gap` divs of estimated height, each one an IntersectionObserver target that mounts its own chunk before it reaches the screen, and holds the sentence being read still when a chunk above it mounts — no IntersectionObserver means the whole chapter, as before) and position sync with the server (`useReaderPosition.ts`, `useSentenceTranslations.ts`); `useAutoImport.ts` — opening a book with no dictionary builds one in the background. `src/fb2/wordExtractor.ts` — the word-extraction worker behind a promise, shared by the import screen and the reader.
  `src/books/` — everything format-dependent, and the only place that branches on format: `zip.ts` (fflate), `decode.ts` (`decodeXml` — the declared encoding, since fb2 books are often windows-1251), `epub.ts` (container → OPF → spine; one XHTML document per chapter, titles from the book's own TOC), `formatError.ts` (`BookFormatError`: `invalid` | `encrypted` — DRM), `format.ts` (`readBookSource`: the bytes decide the format, never the extension) and `toParsedBook.ts` (→ the import's `ParsedBook`). The reader's own shape is built in `reader/readerBook.ts`.
- `extract.py` — Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under `dictionaries/`.

## Runtime

- Postgres via `compose.yaml`. `LanguageLab.Api` reads `ConnectionStrings:DefaultConnection`,
  `Telegram:ClientId` and `Telegram:ClientSecret` from `appsettings.json`/`appsettings.Development.json`
  (the latter is gitignored, local only); in Docker the same keys come from env vars via the `__`
  convention. `WebUser:TelegramId` is gone — there is no config user any more.
- Auth is Telegram over OpenID Connect → an HttpOnly `ll_session` cookie holding an internal user
  id and role. `ICurrentUser` reads those claims; `ICurrentUserContext` adds the role. The first
  successful login becomes the admin. Bans take effect on the next request via the cookie's
  `OnValidatePrincipal`. The cookie is persistent (30 days, sliding — `OnSigningIn` sets
  `IsPersistent`), and the Data Protection keys that encrypt it live in the `DataProtectionKeys`
  table, so a redeploy does not sign everyone out. Development can use the same real flow — `http://localhost:5173/...` is
  a registered Allowed URL in @BotFather — or `GET /api/auth/dev-login`, a local sign-in as a
  dedicated account (Telegram id 1) that skips the handshake. It is fenced off from production
  three times over (`#if DEBUG` + Release publish, `IsDevelopment()`, `import.meta.env.DEV`) —
  see `LanguageLab.Api/Auth/DevLogin.cs`; weakening any fence is a security change.
  Telegram credentials are therefore optional in Development and required everywhere else.
- Mini App sign-in: opened inside Telegram, the SPA posts `window.Telegram.WebApp.initData` to
  `POST /api/auth/telegram/webapp`; `LanguageLab.Api/Auth/WebAppInitData.cs` checks the
  HMAC-SHA256 against `Telegram:BotToken` (required outside Development, like the OIDC
  credentials), refuses launches older than 24 h, and issues the same `ll_session` cookie via
  `UserLoginService`. The SPA asks `/api/auth/me` first and posts only on a 401
  (`web/src/auth/useAuth.ts`; `web/src/auth/telegram.ts` is the only file touching
  `window.Telegram`). Telegram Web (browser iframe) is unsupported: `SameSite=Lax`.
- Roles (`UserRole`: `User`, `Admin` — stored as an int). The `Importer` policy is gone, and so
  is the `Uploader` role (dropped by the `DropUploaderRole` migration, which moves any account
  still holding it back to `User`): importing a book takes no role at all, any signed-in user
  may, and the publication queue — not a role — is what keeps an unreviewed import out of other
  people's way. `Admin` is the only named policy left (`LanguageLab.Api/Auth/AuthPolicies.cs`).
  `UserRoles.CanPublishDirectly(role)` (`LanguageLab.Domain/Entities/UserRoles.cs`), mirrored by
  `web/src/auth/roles.ts`'s `canPublishDirectly`, is now true for `Admin` alone; it stays a named
  predicate because the question at its call sites is whether an import skips review, not whether
  the importer curates. Admins set a user's role from the admin screen's picker.
- Dictionaries have an owner and a `PublicationStatus` (`Private | Pending | Published |
  Rejected`, default `Private`) in place of the old `IsPublic` flag. `POST /api/dictionaries/import`
  takes no role; its `requestPublication` flag only asks —
  `BookImportService.StatusFor(role, requestPublication)` decides the outcome: `Private` when
  publication wasn't requested, `Published` when it was and `CanPublishDirectly(role)`, `Pending`
  otherwise. The owner offers or withdraws the request
  (`POST|DELETE /api/dictionaries/{id}/publication`); an admin decides from the moderation queue
  (`GET /api/admin/dictionaries`, `POST /api/admin/dictionaries/{id}/approve|reject`). Non-admins
  see `Published` dictionaries plus their own; admins see every non-personal one. Delete
  (`DELETE /api/dictionaries/{id}`) stays admin-only and now also removes any shared `WordPair`
  rows the deletion orphans — rows left in no dictionary and carrying no shelf/progress/training
  row for anyone (`DictionaryDeletionService`); `DELETE /api/admin/users/{id}/dictionaries`
  bulk-deletes a user's dictionaries the same way, for use alongside a ban.
- Import validation (`BookImportService`, `LanguageLab.Domain`): a word must be lowercase ASCII
  letters, 3-64 characters (`ImportWordText`) or it's dropped from the import; an import where
  more than 20% of its distinct words are invalid is refused outright. Limits: 50,000 distinct
  words and 2,000 chapters per import (`BookImportService.MaxWords`/`MaxChapters`), names and
  chapter titles truncated at 300 characters (`TitleText.MaxLength`), a 16 MB request-body cap on
  `/api/dictionaries/import` itself, and bulk personal-word import capped at 500 entries
  (`PersonalDictionaryService.MaxBulkEntries`). `Dictionary.FileHash` (set on import, a SHA-256 of
  the fb2 — see "The reader" below) is kept only when the importer's own `ReaderBook` library
  already has that hash; otherwise it's dropped even if the client sent one. This is not proof
  the hash is genuine — a client can register any hash first — but it raises the bar past a
  casual collision or a drive-by import with no `ReaderBook` at all. The real backstop against a
  stranger's junk import reaching other readers is publication review, not this check: an
  unreviewed import is `Private`, invisible to everyone but its owner and admins regardless of
  what hash it claims.
- Personal dictionary: one private `Dictionary` per user (`IsPersonal`, created on first use by
  `GET /api/dictionaries`), words are `WordPair` rows with `OwnerId` set (unique on `(Word, OwnerId)`,
  `NULLS NOT DISTINCT`), shelved "don't know" on add. Shared vocabulary = `OwnerId IS NULL`; any
  lookup by word text must filter on it. `Translation:MyMemoryEmail` and `Translation:DeepLApiKey`
  are optional config.
- The reader: the book file stays in the browser (IndexedDB), never uploaded. The server keeps
  only `ReaderBook` — the file's SHA-256 (`ReaderHash`), title, author, chapter count, and the
  reading position (chapter/paragraph/sentence index plus a 0..1 progress); on a position update
  from any device, the `clientUpdatedAt` timestamp decides and the later one wins, capped at the
  server's own clock. `Dictionary.FileHash` is set on import (also a SHA-256 of the book file), so the
  reader can point a book at the dictionary it was imported as; dictionaries imported before this
  feature have none, so `ReaderBookService` falls back to a visible, non-personal dictionary whose
  `Name` equals the book's title — a hash match always wins over this name match — so opening or
  importing such a book links the existing dictionary instead of duplicating it.
  `Translation:DeepLApiKey` is optional — without it MyMemory translates sentences too, capped by
  `MyMemorySentenceBudget`: a server-wide daily budget of 40 % of MyMemory's own daily limit
  (2 000 characters without `Translation:MyMemoryEmail`, 20 000 with it), spent only on a sentence
  actually sent, so the reader cannot exhaust the quota the word lookups also share; the word
  lookups get the remaining 60 % as `MyMemoryWordBudget`, the same `DailyCharacterBudget`
  mechanism (`LanguageLab.Application/Translation/DailyCharacterBudget.cs`) wrapped the other way
  round. Per-user daily rate limits (`UserRateLimits`, sliding windows, in-memory like
  `SentenceQuota`) sit in front of the three endpoints one account could otherwise make expensive
  for everybody: 20 `/api/dictionaries/import` requests, 500 `GET /api/translate` lookups, 20
  bulk personal-word-import requests, per day. Provider word
  translations (`TranslationService.LookupAsync`, used both by `GET /api/translate` and the
  reader's word panel) are cached into the shared vocabulary as a `WordPair` row with
  `TranslationOrigin = Machine`, so the same word is looked up at most once; a `Manual` translation
  already on that row is never overwritten. Training only pulls new words from a specific
  dictionary, so a cached word only becomes trainable once it belongs to one — the reader's Add to
  training shelves a word only in the dictionary of the book being read
  (`ReaderWordService.LearnTargetAsync`), otherwise it goes to "My words" with the cached
  translation. Opening a book in the reader (outside Telegram) builds its dictionary in the
  background for any signed-in user — private, named after the book, leaf chapters, and bounded
  by the same 20-imports-a-day limit as a manual import — and the import screen also puts an
  imported book in the reader.
- Migrations run automatically on startup (`dbContext.Database.MigrateAsync()` in [Program.cs:39](LanguageLab.Api/Program.cs#L39)).
- The irregular-verbs trainer is a separate domain from dictionaries: the 68 verbs and their examples live in code (`LanguageLab.Domain/IrregularVerbs/IrregularVerbCatalog`), grouped into 4 pattern groups split into 15 families, every one open from the start (`LearningPath` only tells done families — 80 % learned — from available ones; nothing is locked). A user's standing on a verb (`VerbProgress`: `New → Learning1 → Learning2 → Learning3 → Learned`, or `Forgotten` via the Forgot button) drives `SessionPlanner`/`ExerciseFactory`, which build a stored queue of tasks (`VerbSession`/`VerbTask`) — seven exercise types (card, gap-fill choice, odd-one-out, match, form-pick, typed gap-fill, all-three-forms typing) with typical-mistake distractors (`DistractorGenerator`). `AnswerChecker` grades server-side and names the mistake (`ErrorKind`); every attempt is logged append-only (`VerbAttempt`). A mistake reinserts the verb 2 and 5 tasks later in the same session. `/api/irregular-verbs` (`GET /progress`, `POST /sessions`, `GET /sessions/{id}/next`, `POST /sessions/{id}/answer`, `POST /sessions/{id}/finish`, `POST /verbs/{v1}/forgot`). No `WordPair`, no dictionary, no seeding — the earlier per-form trainer's table was dropped and replaced by the `IrregularVerbTrainer` migration.
- The pronunciation trainer is a separate domain from dictionaries and from the irregular-verbs
  trainer: a curated catalog of English words (`LanguageLab.Domain/Pronunciation/PronunciationCatalog`,
  generated by `generate_pronunciation_catalog.py` — CMU-dictionary-driven set-cover word selection
  per sound family, real US/UK recordings from Wiktionary, committed under
  `web/public/pronunciation-audio/`), grouped into sound families that are all open from the start
  (`PronunciationLearningPath` only marks one done at 80 % mastered; nothing is locked). A user's standing on a word (`PronunciationProgress`:
  `New → Learning → Mastered`) drives which word is served next; an attempt is graded server-side
  by comparing the browser's `SpeechRecognition` transcript against the target word
  (`PronunciationAnswerChecker` — a word-recognition proxy for pronunciation quality, not
  phoneme-level scoring), and every attempt is logged append-only (`PronunciationAttempt`).
  `/api/pronunciation` (`GET /progress`, `GET /families/{key}`, `GET /families/{key}/next`,
  `POST /words/{word}/attempts`, `DELETE /words/{word}/progress` — the word card's Reset
  progress, which drops the standing and keeps the attempt log). Gated off entirely in browsers without `SpeechRecognition`
  (Firefox, Safari) — no degraded fallback mode.
- Training requires a non-empty `WordPair.Translation` (both for batch words and distractors). Translations for the "don't know" shelf were backfilled once on 2026-09-07 (`result/translations.txt`, local); auto-translation is in the README TODO.
- Batch = the scope's most frequent learnable words (chapter or book frequency), deterministic; the web app shows a preview and passes `wordPairIds` explicitly.
- Version: one place, the `<Version>` element of `Directory.Build.props` at the repo root. MSBuild
  applies it to every project; `web/vite.config.ts` reads the same element and bakes it into the
  SPA as `__APP_VERSION__`, read only through `web/src/lib/version.ts` and shown next to the logo
  in the top bar. CI rewrites that element to `0.0.0.N` before the docker build — README →
  Versioning. Both Dockerfiles copy the file into their build stages; keep it that way.

## Adding a migration

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.Api migrations add <Name>
```

## TODO backlog

`README.md` has a `## TODO` section — a backlog of short topics. Rules:

- Deferred functionality (a stub, an inactive button, "we'll do it later") — add one line there **in the same set of changes**. Check first whether that item is already there.
- The user says "do something from the TODO" / "take a TODO item" — pick from there (the first open one, unless a different one is specified), mark it `[x]` once done.
- Items are short, with a place in the code where they belong, if that's already known.

## Frontend conventions (`web/`)

- Design tokens (colors, typography, radii, motion) — only in `web/src/index.css`; component CSS uses only `var(--…)`, no hex. Light/dark theme — via `prefers-color-scheme`.
  `web/src/index.contrast.test.ts` reads the tokens straight out of the CSS and fails if a button's label drops below WCAG AA (4.5:1) in either theme, at rest or hovered — add new filled buttons to its `BUTTONS` table.
- CSS lives next to its component and is imported from its `.tsx`. New buttons — via `.btn` + `.btn-primary | .btn-secondary | .btn-quiet` (+ `.btn-lg`).
- UI copy — English, sentence case, verb-first button labels. Keyboard shortcuts are shown as a separate `<kbd>`, not inline in the text.
- Numbers — via `formatInt`/`wordsLabel` from `web/src/lib/format.ts`, class `.num` for tabular figures.
- Routing — an in-memory `Route` in `App.tsx`; no react-router. Don't add new npm dependencies without asking the user.
- Check before handing off: `cd web && npm run lint && npm test && npm run build`.

## Python pipeline

Managed with `uv`. Requires the `en_core_web_sm` spaCy model — see [README.md](README.md#python-environment) for the install line.
