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
- `LanguageLab.Application/` — services on top of the domain: word selection, training sessions, book import, sorting, per-scope Leitner progress (`LearningProgressService`). Used by the API.
- `LanguageLab.Api/` — ASP.NET Core Minimal API + serves the SPA. Runs DB migrations. Endpoints: `/api/dictionaries`, `/api/sorting`, `/api/training` (Leitner quiz on top of `TrainingSessionService`; the question queue lives in the DB); `GET /api/training/preview` — scope's progress scale + batch candidates by frequency, `new-batch` accepts explicit `wordPairIds`.
  `Auth/` (claims, session validation, the OIDC event handlers), `/api/auth/*`
  (telegram/start, the handler-owned telegram/callback, me, logout), `/api/admin/users*`
  (list, ban, unban, role, delete).
- `web/` — React + Vite SPA: fb2 import in the browser, dictionary stats, word sorting. `src/layout/` (shell: top bar + sidebar), `src/screens/`, `src/components/`, `src/lib/` (formatters), tests `*.test.ts(x)` next to the code (vitest + jsdom, helper `src/test/render.ts`). Details in [web/README.md](web/README.md).
- `extract.py` — Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under `dictionaries/`.

## Runtime

- Postgres via `compose.yaml`. `LanguageLab.Api` reads `ConnectionStrings:DefaultConnection`,
  `Telegram:ClientId` and `Telegram:ClientSecret` from `appsettings.json`/`appsettings.Development.json`
  (the latter is gitignored, local only); in Docker the same keys come from env vars via the `__`
  convention. `WebUser:TelegramId` is gone — there is no config user any more.
- Auth is Telegram over OpenID Connect → an HttpOnly `ll_session` cookie holding an internal user
  id and role. `ICurrentUser` reads those claims; `ICurrentUserContext` adds the role. The first
  successful login becomes the admin. Bans take effect on the next request via the cookie's
  `OnValidatePrincipal`. Development uses the same real flow — `http://localhost:5173/...` is a
  registered Allowed URL in @BotFather.
- Dictionaries have an owner and an `IsPublic` flag: import, delete and visibility changes are
  admin-only, and regular users see public dictionaries plus their own.
- Migrations run automatically on startup (`dbContext.Database.MigrateAsync()` in [Program.cs:39](LanguageLab.Api/Program.cs#L39)).
- Training requires a non-empty `WordPair.Translation` (both for batch words and distractors). Translations for the "don't know" shelf were backfilled once on 2026-09-07 (`result/translations.txt`, local); auto-translation is in the README TODO.
- Batch = the scope's most frequent learnable words (chapter or book frequency), deterministic; the web app shows a preview and passes `wordPairIds` explicitly.

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
