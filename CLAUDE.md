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
- `LanguageLab.Application/` — services on top of the domain: word selection, training sessions, book import, sorting, per-scope Leitner progress (`LearningProgressService`), the irregular-verbs trainer (`VerbProgressService`, `VerbSessionService`); `ChapterStatsService` (the per-chapter rows of a book, also for a subset), `StarredChapterService` (a user's starred chapters); `PersonalDictionaryService` (the user's own word list) and `Translation/` (`ITranslator` → `MyMemoryTranslator`, `TranslationService` — shared vocabulary first, provider after; `ISentenceTranslator` → `DeepLTranslator`, the reader's sentence translation). The reader's own services: `ReaderBookService` (the reader's library — file hash, title, author, position, and the dictionary the same file was imported as, if any), `ReaderWordStatusService` (a word's New/Learning/Known standing, derived from the same Leitner/shelf/personal rows training uses) and `ReaderWordService` (the word panel's lookup, and its Learn/Know buttons — shelves a word already linked to a dictionary, and otherwise adds it to "My words", translated or not). Used by the API.
- `LanguageLab.Api/` — ASP.NET Core Minimal API + serves the SPA. Runs DB migrations. Endpoints: `/api/dictionaries`, `/api/sorting`, `/api/training` (Leitner quiz on top of `TrainingSessionService`; the question queue lives in the DB; the SPA grades each answer in the browser from the question's own `wordPairId` and posts it in the background, prefetching the next question behind it — `TrainingScreen.tsx`); `GET /api/training/preview` — scope's progress scale + batch candidates by frequency, `new-batch` accepts explicit `wordPairIds`.
  `POST /api/training/review` takes an optional `{ dictionaryId, chapterIds }` body: without it the review is global (started from the home screen's "Due today" tile), with it only that scope's due words — the book header reviews the book, a chapter row its chapter once it has no new words left.
  `GET /api/training/stats` — the user's global Leitner standing (per-box counts, learned, due), rendered on the home screen.
  `/api/chapters` — `GET /starred` (the home screen's starred chapters, ordered by book name then chapter order), `PUT|DELETE /{id}/star` (204; 404 for a chapter of an invisible book, or nothing to unstar).
  `Auth/` (claims, session validation, the OIDC event handlers), `/api/auth/*`
  (telegram/start, the handler-owned telegram/callback, telegram/webapp, me, logout), `/api/admin/users*`
  (list, ban, unban, role, delete);
  `GET /api/dictionaries/personal`, `POST|DELETE /api/dictionaries/personal/words[/{id}]` — the personal dictionary; `GET /api/translate?word=` — a translation suggestion.
  `/api/reader` — `GET /capabilities` (whether sentence translation is configured), `GET /books` (the reader's library), `PUT /books/{hash}` (register/refresh a book by its file hash, idempotent), `DELETE /books/{hash}`, `PUT /books/{hash}/position`, `GET /word-statuses`, `GET /words/{lemma}` (the word panel's lookup), `POST /words/{lemma}/learn`, `POST /words/{lemma}/known`. `POST /api/translate/sentence` — DeepL sentence translation for the reader, 404 without a configured key, 429 past the user's 20 000-characters-a-day quota (`SentenceQuota`), nothing stored.
- `LanguageLab.TgBot/` — the Telegram bot: a Generic Host console app on `Telegram.Bot` (long polling), no database, no project references. `/start` (any private message) answers with the bot name, a description and an **Open LanguageLab** `web_app` button; the chat menu button is set to the same URL at startup. Config `Telegram:BotToken`, `WebApp:Url` (`BotOptions`); copy and keyboard in `StartMessage`; polling in `BotService`. Own Dockerfile and compose service.
- `web/` — React + Vite SPA: fb2 import in the browser, dictionary stats, word sorting. `src/layout/` (shell: top bar + sidebar), `src/screens/`, `src/components/`, `src/lib/` (formatters), tests `*.test.ts(x)` next to the code (vitest + jsdom, helper `src/test/render.ts`). Details in [web/README.md](web/README.md).
  `src/reader/` — the Reading mode: its own fb2 parser (`readerBook.ts`, sentence/word tokenizing, `.fb2.zip` refused with `BookFormatError`) and local storage (`bookStore.ts`, the book files themselves, IndexedDB — never uploaded — and `readerSettings.ts` for theme/dimming/font size), the library screen (`ReaderLibraryScreen.tsx`) and the full-screen reader (`ReaderScreen.tsx`, `Sentence.tsx`, `WordPanel.tsx`, `ReaderMenu.tsx`), word-status resolution (`wordStatus.ts`) and position sync with the server (`useReaderPosition.ts`, `useSentenceTranslations.ts`).
- `extract.py` — Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under `dictionaries/`.

## Runtime

- Postgres via `compose.yaml`. `LanguageLab.Api` reads `ConnectionStrings:DefaultConnection`,
  `Telegram:ClientId` and `Telegram:ClientSecret` from `appsettings.json`/`appsettings.Development.json`
  (the latter is gitignored, local only); in Docker the same keys come from env vars via the `__`
  convention. `WebUser:TelegramId` is gone — there is no config user any more.
- Auth is Telegram over OpenID Connect → an HttpOnly `ll_session` cookie holding an internal user
  id and role. `ICurrentUser` reads those claims; `ICurrentUserContext` adds the role. The first
  successful login becomes the admin. Bans take effect on the next request via the cookie's
  `OnValidatePrincipal`. Development can use the same real flow — `http://localhost:5173/...` is
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
- Roles (`UserRole`: `User`, `Admin`, `Uploader` — appended, the column is an int): an uploader
  may import books and nothing more. The named policies live in
  `LanguageLab.Api/Auth/AuthPolicies.cs` (`Admin`, `Importer` = admin or uploader); the SPA
  mirrors them in `web/src/auth/roles.ts` (`canImport`, `roleLabel`). Admins set a user's role
  from the admin screen's picker.
- Dictionaries have an owner and an `IsPublic` flag: import is for admins and uploaders, delete
  and visibility changes are admin-only, and non-admins see public dictionaries plus their own.
- Personal dictionary: one private `Dictionary` per user (`IsPersonal`, created on first use by
  `GET /api/dictionaries`), words are `WordPair` rows with `OwnerId` set (unique on `(Word, OwnerId)`,
  `NULLS NOT DISTINCT`), shelved "don't know" on add. Shared vocabulary = `OwnerId IS NULL`; any
  lookup by word text must filter on it. `Translation:MyMemoryEmail` and `Translation:DeepLApiKey`
  are optional config.
- The reader: the fb2 file stays in the browser (IndexedDB), never uploaded. The server keeps
  only `ReaderBook` — the file's SHA-256 (`ReaderHash`), title, author, chapter count, and the
  reading position (chapter/paragraph/sentence index plus a 0..1 progress); on a position update
  from any device, the `clientUpdatedAt` timestamp decides and the later one wins, capped at the
  server's own clock. `Dictionary.FileHash` is set on import (also a SHA-256 of the fb2), so the
  reader can point a book at the dictionary it was imported as; dictionaries imported before this
  feature have none, and the reader library then just shows the book without a linked dictionary.
  `Translation:DeepLApiKey` is optional — without it `GET /api/reader/capabilities` reports
  sentence translation unavailable and the SPA hides the feature entirely. Provider word
  translations (`TranslationService.LookupAsync`, used both by `GET /api/translate` and the
  reader's word panel) are cached into the shared vocabulary as a `WordPair` row with
  `TranslationOrigin = Machine`, so the same word is looked up at most once; a `Manual` translation
  already on that row is never overwritten. Training only pulls new words from a specific
  dictionary, so a cached word only becomes trainable once it belongs to one — the reader's Learn
  button shelves it when it already does, and otherwise adds it to the user's "My words" instead
  (`ReaderWordService.LearnAsync`), using the cached translation.
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
  `POST /words/{word}/attempts`). Gated off entirely in browsers without `SpeechRecognition`
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
- CSS lives next to its component and is imported from its `.tsx`. New buttons — via `.btn` + `.btn-primary | .btn-secondary | .btn-quiet` (+ `.btn-lg`).
- UI copy — English, sentence case, verb-first button labels. Keyboard shortcuts are shown as a separate `<kbd>`, not inline in the text.
- Numbers — via `formatInt`/`wordsLabel` from `web/src/lib/format.ts`, class `.num` for tabular figures.
- Routing — an in-memory `Route` in `App.tsx`; no react-router. Don't add new npm dependencies without asking the user.
- Check before handing off: `cd web && npm run lint && npm test && npm run build`.

## Python pipeline

Managed with `uv`. Requires the `en_core_web_sm` spaCy model — see [README.md](README.md#python-environment) for the install line.
