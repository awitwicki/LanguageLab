# LanguageLab

Web app for learning new words from books. Users pick a dictionary extracted from a book, then learn words through procedural exercises in the SPA.

## Rules for Claude agents

- **Never commit or push** until the user explicitly asks. Show the diff, wait for approval.
- Do not run `git add`, `git commit`, `git push`, or any history-rewriting command on your own.
- When the user has allowed committing step-by-step during multi-step work (e.g. executing a plan — one commit per task), once all steps are done, squash those commits into one (`git reset --soft` to the state before the first of them, then commit again as a single commit) **before considering the work done**. The user reviews one commit before push, not a series. This is squashing intermediate commits within already-approved work, not standalone permission for a new commit — but the push itself still waits for a separate request.
- **Branch: new features and fixes go on `dev`, never `main`.** `main` only takes a quick hotfix, and only with the user's explicit go-ahead for landing on `main` specifically — the general "yes, commit" does not imply `main`. Check the current branch before committing (`git branch` / `git status`); if it's `main` and the work isn't an approved hotfix, switch to `dev` first rather than committing where you are.
- Creating migrations is fine (`dotnet ef ... migrations add`), but `--startup-project` is now `LanguageLab.Api`.
- **Model per phase.** Brainstorming, writing specs and writing plans want a strong reasoning model. Implementing a plan (native execution) wants Sonnet — cheaper, and the plan already carries the design. Before starting implementation of an approved plan, **stop and remind the user to switch to Sonnet** if a stronger model is currently active; wait for their go-ahead (they'll say once it's switched) rather than switching yourself or guessing from context which model is live.
- Everything in the project — code, comments, doc-comments, commit messages, documentation — is English. The one exception: the vocabulary translation shown to the learner (`WordTranslation.Text`) is in their chosen language (`TelegramUser.Language`, a `LearnerLanguages` code), since teaching English to speakers of that language is the whole point of the app. UI copy in the SPA is also English now (see "Frontend conventions" below).

## Project layout

| Path | What it is |
|---|---|
| `LanguageLab.Domain/` | Entities (`Dictionary`, `WordPair`, `KnownWord`, `UnknownWord`, `TelegramUser`, `Training`, `TrainingEvent`) and interfaces, plus the code-only verb, pronunciation, IPA and grammar catalogs and the `LearnerLanguages` language catalog. No dependencies on infrastructure. |
| `LanguageLab.Infrastructure/` | EF Core `ApplicationDbContext`, PostgreSQL provider, migrations. |
| `LanguageLab.Application/` | Services on top of the domain: word selection, training sessions, book import and the fb2/epub parsers behind it (`Books/`), sorting, Leitner progress, translation, the reader's services, the two trainers. |
| `LanguageLab.Api/` | ASP.NET Core Minimal API + serves the SPA. Runs DB migrations. `Auth/` holds the claims, session validation and OIDC handlers. |
| `LanguageLab.TgBot/` | The Telegram bot: a long-polling Generic Host console app, no database, no project references. |
| `web/` | React + Vite SPA: book import in the browser, dictionary stats, word sorting, Reading mode (`src/reader/`, `src/books/`). |
| `extract.py` | Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under `dictionaries/`. |
| `scripts/build_lexicon.py` | Generates the English lexicon (`english-lexicon.txt`, form → lemmas) from SCOWL + AGID; `scripts/lexicon-overrides.txt` holds the hand-kept fixes. Stdlib-only Python, run with `uv`. |

The full map — every service's responsibility and the whole endpoint inventory — is in
[docs/architecture.md](docs/architecture.md).

## Invariants

These hold wherever you are working, whatever you are changing:

- **Shared vocabulary is `OwnerId IS NULL`.** Any lookup by word text must filter on it; a
  `WordPair` with `OwnerId` set belongs to one user's personal dictionary (unique on
  `(Word, OwnerId)`, `NULLS NOT DISTINCT`).
- **Training requires a `WordTranslation` in the learner's language** — for batch words and
  distractors alike; a session renders in `Training.Language`.
- **Russian is never a learner language**: it is absent from `LearnerLanguages`, and the
  irregular-verbs trainer is Ukrainian-only.
- **Import validation** (`BookFileImportService`, `ImportTokenizer`, `BookImportService`,
  `LanguageLab.Domain`): the server parses the uploaded file itself (`IBookParser`), tokenizes it
  and keeps only lemmas the English lexicon knows (`IEnglishLexicon`) — a stored word is lowercase
  ASCII letters, 2-64 characters (`ImportWordText`), and an upload whose text is less than half
  lexicon-known occurrences is refused as not English. Caps: 50,000 distinct words, 2,000
  chapters, 300-character names and chapter titles (`TitleText.MaxLength`), a 16 MB upload,
  1 import/day (admins exempt), and 500 entries per bulk personal-word import.
- **`english-lexicon.txt` is generated.** It has two copies, `LanguageLab.Domain/Lexicon/` and
  `web/public/lexicon/`, written by `scripts/build_lexicon.py` — never edit it by hand; fix a lemma
  in `scripts/lexicon-overrides.txt` and rerun; a test keeps the two copies identical.
- **Book parsing exists twice** — `web/src/books/` + `web/src/fb2/chapters.ts` (the reader and the
  import preview) and `LanguageLab.Application/Books/` (the server's import) — and both must agree
  on chapters and text; change them together.
- **`GET /api/auth/dev-login` is fenced off from production three times over** — `#if DEBUG` plus a
  Release publish, `IsDevelopment()`, and `import.meta.env.DEV`
  (`LanguageLab.Api/Auth/DevLogin.cs`). Weakening any fence is a security change.
- **CSRF is `SameOriginGuard`** (`LanguageLab.Api/Auth/SameOriginGuard.cs`): every `/api` request
  must be same-origin by `Sec-Fetch-Site` (or `Origin`, for writes from a browser that sends no
  fetch metadata); only the OIDC callback is exempt. A new cross-site entry point is a security
  change.
- **`Admin` is the only named policy** (`LanguageLab.Api/Auth/AuthPolicies.cs`). Importing a book
  takes no role at all; the publication queue, not a role, is what keeps an unreviewed import out of
  other people's way.
- **Migrations run automatically on startup** — `dbContext.Database.MigrateAsync()` in
  [Program.cs:39](LanguageLab.Api/Program.cs#L39).
- **The version lives in one place**: the `<Version>` element of `Directory.Build.props` at the repo
  root. MSBuild applies it to every project, and `web/vite.config.ts` reads the same element and
  bakes it into the SPA as `__APP_VERSION__`, read only through `web/src/lib/version.ts` and shown
  next to the logo. Both Dockerfiles copy the file into their build stages; keep it that way. CI
  rewrites the element to `0.0.0.N` before the docker build, and how to bump it by hand is in
  README → Versioning.

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

## Where the details live

Nothing below is loaded for you automatically — open the one that matches the task.

| File | Open it when |
|---|---|
| [docs/architecture.md](docs/architecture.md) | You need the project map, what a service owns, the endpoint inventory, or the Postgres/config setup. |
| [docs/auth.md](docs/auth.md) | Touching sign-in, the session cookie, CSRF (`SameOriginGuard`), dev-login, the Mini App and its full-screen insets, or roles. |
| [docs/vocabulary-and-training.md](docs/vocabulary-and-training.md) | Working on import, publication and moderation, the personal dictionary, the LLM translator, the background translation queue and per-user limits, or Leitner batches. |
| [docs/reader.md](docs/reader.md) | Working in Reading mode: `ReaderBook` and position sync, the word panel, chapter windowing, fb2/epub parsing. |
| [docs/trainers.md](docs/trainers.md) | Working on the irregular-verbs, pronunciation or grammar trainer. |
| [docs/grammar-roadmap.md](docs/grammar-roadmap.md) | Planning grammar content or the next grammar phase: the A1–B1 syllabus and the copyright rules. |
| [web/README.md](web/README.md) | You need SPA specifics beyond the conventions above. |
| [README.md](README.md) | Running the app, Docker, versioning, and the TODO backlog itself. |
