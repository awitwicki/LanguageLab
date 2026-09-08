# LanguageLab

Web app for learning new words from books

1. Pick dictionary
2. learn new words
3. GOTO 1

## TODO

Backlog of short topics. When something gets deferred (a stub, an inactive button, "we'll do it later") —
add one line here **in the same set of changes**. Done items are marked `[x]`.

- [ ] Account menu in the top bar: currently an inactive button (profile, sign out)
- [x] "Start exercise" on the dictionary screen: Leitner quiz in the web app per chapter or book, batch of 5/10/20 (`/api/training`)
- [ ] Delete dictionary from the UI (`DELETE /api/dictionaries/{id}` already exists)
- [ ] Auto-translate on book import and when marking a word "don't know" + edit translation in the UI (one-off backfill of 2797 "don't know" shelf words done on 2026-09-07; new "don't know" words without a translation don't enter exercises)
- [ ] Show translation on the sorting card (now available for translated words)
- [ ] Resume an unfinished training session after a page reload (the session exists in the DB, no UI entry point yet)
- [ ] Leitner stats: box histogram, learned / due today (`TrainingSessionService.GetStatsAsync` already exists)
- [ ] User stats: total known / learning / excluded counts
- [ ] Color contrast WCAG AA: check `.btn-known`/`.btn-unknown` in light theme and `.btn-primary` on `--accent` in dark theme
- [ ] Focus management on route change: announce the new screen for screen readers (focus currently falls back to `<body>`)
- [ ] Spacing tokens in the design system: `web/src/index.css` tokenizes color/typography/radii, but not spacing
- [ ] Keyboard navigation for the batch-size segment control: role="radio" without arrow keys (ARIA APG for radiogroup) — currently only Tab+Space
- [ ] Move a word back from "know" to "don't know" outside the exercise-start screen: the cross-out in the batch preview (`web/src/training/useBatchPreview.ts`) — "bring back" only works within the current visit; after that the word can only be reached via `POST /api/sorting/mark`
- [ ] GET /api/dictionaries/{id}: 3 COUNT queries per chapter (sorted + learnable) — merge into one GROUP BY if this ever becomes slow
- [ ] Shelf admin panel: list of all words in the DB, list of "know", list of "don't know", list of excluded — with the ability to un-mark (move back between shelves) right there
- [ ] Accounts and registration: currently a single user from `appsettings` (`WebUser:TelegramId`, `LanguageLab.Api/CurrentUser.cs`), no real login yet
- [ ] Ability to exclude a word directly from the "Most frequent words" list on the dictionary screen (`web/src/screens/DictionaryScreen.tsx`) — character names and place names leak in there
- [ ] Caption under the chapter/book progress scale (`web/src/components/LeitnerScale.tsx`): currently unclear that this is specifically word-learning progress, not an arbitrary percentage
- [ ] Home screen: recent exercises with a "Repeat" button, recent dictionaries/chapters that were sorted — so the user can go back and finish sorting them (`web/src/screens/HomeScreen.tsx`)
- [ ] Dictionary visibility: a toggle at import time / in settings — public (visible to all users) or private (only the creator); `Dictionary` (`LanguageLab.Domain/Entities/Dictionary.cs`) currently has no owner or visibility field
- [ ] Irregular-verbs dictionary, split into 4 groups — the user will share a table; confirm the format/group details with the user before implementing
- [ ] Top-100/200/500/1000 English word dictionaries, public
- [x] .fb2 words extractor in the browser (book import with chapters)
- [x] Docker compose, DB migrations
- [x] Sorting words "know / don't know / exclude" per book or chapter

## Development

### Conventions

Everything in the project — code, comments, docs, UI copy — is English. The one exception: the vocabulary translation shown to the learner (`WordPair.Translation`, the Ukrainian meaning of each English word) stays Ukrainian, since that's the point of the app. See `CLAUDE.md` → Frontend conventions.

### Docker / .env

`compose.yaml` brings up Postgres and `LanguageLab.Api` together. Env vars:

* `POSTGRES_PASSWORD={password}` - Postgres password, referenced by `compose.yaml` for both the database container and the API's connection string

**Docker compose:** create `.env` file and fill it with that variable.

### LanguageLab.Api

`LanguageLab.Api` reads its config from `appsettings.json` / `appsettings.Development.json`
(the latter is local, gitignored), not from env vars:

* `ConnectionStrings:DefaultConnection` - postgres connection string
* `WebUser:TelegramId` - the user the web app acts as (no auth yet)

For a local run, fill in `LanguageLab.Api/appsettings.Development.json` (see the example
below). In Docker the same values are passed via env vars using the standard
ASP.NET Core convention (`__` instead of `:`): `ConnectionStrings__DefaultConnection`,
`WebUser__TelegramId`.

## Run

### With Docker (whole stack)

```
docker-compose up --build -d
```

Brings up Postgres and `LanguageLab.Api` (which also serves the web app at `http://localhost:5080`)
in one move.

### Locally, without Docker (dotnet + npm)

Only a running Postgres is needed — easiest to bring up just that in a container
(the rest of the processes run directly on the host):

```bash
docker compose up -d dbpostgres
```

(or point to your own local Postgres — the key requirement is that `ConnectionStrings:DefaultConnection`
in `appsettings.Development.json` points to it; port `5433` is what compose
maps out of the container).

**API** (separate terminal; it also migrates the DB schema on startup and serves the web app
if `web/` is built — in dev mode, use Vite below instead).

First fill in `LanguageLab.Api/appsettings.Development.json` (a local file,
not committed to git):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=LanguageLabTgBot;Username=postgres;Password={password};"
  },
  "WebUser": {
    "TelegramId": {your telegram id}
  }
}
```

```bash
dotnet run --project LanguageLab.Api
```

Comes up at `http://localhost:5080`.

**Web app** (separate terminal; Vite with a proxy to the API, needed for frontend development):

```bash
cd web
npm install
npm run dev      # http://localhost:5173
npm test         # vitest
```

### Database migrations

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.Api migrations add {migrationName}
```

## Python environment

```bash
pip install uv
uv init
uv sync
uv pip install -r requirements.txt
uv pip install https://github.com/explosion/spacy-models/releases/download/en_core_web_sm-3.0.0/en_core_web_sm-3.0.0.tar.gz
uv run extract.py 
uv run sort_words.py
```

`extract.py` - extract words in base from fb2 file and save to txt file
