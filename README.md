# LanguageLab

Web app for learning new words from books

1. Pick dictionary
2. learn new words
3. GOTO 1

## TODO

Backlog of short topics. When something gets deferred (a stub, an inactive button, "we'll do it later") —
add one line here **in the same set of changes**. Done items are marked `[x]`.

- [ ] Possibility to star chapters for fast access on dashboard, and minitable with starred chapteers in dict page
- [ ] Auto-translate on book import and when marking a word "don't know" + edit translation in the UI — `TranslationService` (`LanguageLab.Application/Translation`) now exists for single words; wire it into `BookImportService` / `WordSortingService.MarkAsync` (one-off backfill of 2797 "don't know" shelf words done on 2026-09-07; new "don't know" words without a translation don't enter exercises)
- [ ] Resume an unfinished training session after a page reload (the session exists in the DB, no UI entry point yet)
- [ ] Color contrast WCAG AA: check `.btn-known`/`.btn-unknown` in light theme and `.btn-primary` on `--accent` in dark theme
- [ ] Spacing tokens in the design system: `web/src/index.css` tokenizes color/typography/radii, but not spacing
- [ ] Move a word back from "know" to "don't know" outside the exercise-start screen: the cross-out in the batch preview (`web/src/training/useBatchPreview.ts`) — "bring back" only works within the current visit; after that the word can only be reached via `POST /api/sorting/mark`
- [ ] GET /api/dictionaries/{id}: 3 COUNT queries per chapter (sorted + learnable) — merge into one GROUP BY if this ever becomes slow
- [ ] Shelf admin panel: list of all words in the DB, list of "know", list of "don't know", list of excluded — with the ability to un-mark (move back between shelves) right there
- [ ] Home screen: recent exercises with a "Repeat" button, recent dictionaries/chapters that were sorted — so the user can go back and finish sorting them (`web/src/screens/HomeScreen.tsx`)
- [ ] Irregular-verbs review ("tonus") mode: SM-2-style scheduling (ease, interval, next-review date), the `Mastered` state, and the priority score for picking which due verbs to show (`VerbProgress.Ease`/`IntervalDays`/`NextReviewAt` are already stored, not read yet)
- [ ] Irregular-verbs final exam: one-time 40-question mixed session after all groups are learned, 85% to pass, failing verbs become `Forgotten`
- [ ] Irregular-verbs RAPID and SENTENCE_BUILD exercise types (not in the Phase 1 exercise catalog)
- [ ] Irregular-verbs "My words" screen: every verb with a state/group filter and a way to un-flag or jump straight to reviewing it
- [ ] Irregular-verbs statistics screen: mistakes by group, family and `ErrorKind` (`VerbAttempt` already logs everything needed)
- [ ] Irregular-verbs response-time signal: `VerbAttempt.ResponseMs` is logged but not used by the (future) review scheduling
- [ ] Top-100/200/500/1000 English word dictionaries, public
- [ ] Remove the diagnostic Telegram claims dump from `TelegramAuth.OnTokenValidatedAsync` (`LanguageLab.Api/Auth/TelegramAuth.cs`) once a real sign-in confirms whether the numeric Telegram id arrives as the `id` or the `sub` claim — it logs every profile claim verbatim, and `ReadIdentity` may need the name corrected
- [ ] Comment sweep: code comments are still Ukrainian in ~190 lines across `web/src` (plus `useBatchPreview.test.ts` test names), 36 backend `.cs` files (`Domain` mostly), `Program.cs` and the `Dockerfile` — comments only, no UI copy
- [ ] Edit a personal word's translation after it was added (`web/src/screens/PersonalDictionaryScreen.tsx`, `PersonalDictionaryService`)
- [ ] Cache provider translations into shared `WordPair` rows so the same word is not looked up twice (`TranslationService.LookupAsync`)

## Development

### Conventions

Everything in the project — code, comments, docs, UI copy — is English. The one exception: the vocabulary translation shown to the learner (`WordPair.Translation`, the Ukrainian meaning of each English word) stays Ukrainian, since that's the point of the app. See `CLAUDE.md` → Frontend conventions.

### Docker / .env

`compose.yaml` brings up Postgres and `LanguageLab.Api` together. Env vars:

* `POSTGRES_PASSWORD={password}` - Postgres password, referenced by `compose.yaml` for both the database container and the API's connection string
* `TELEGRAM_CLIENT_ID` / `TELEGRAM_CLIENT_SECRET` - OpenID Connect credentials, passed through to `Telegram:ClientId` / `Telegram:ClientSecret` (see Accounts, below)
* `Translation__MyMemoryEmail` - optional; any contact email raises MyMemory's free
  translation quota from 5 000 to 50 000 characters a day per server IP (see Translation, below)

**Docker compose:** create `.env` file and fill it with those variables.

### LanguageLab.Api

`LanguageLab.Api` reads its config from `appsettings.json` / `appsettings.Development.json`
(the latter is local, gitignored), not from env vars:

* `ConnectionStrings:DefaultConnection` - postgres connection string
* `Telegram:ClientId` / `Telegram:ClientSecret` - OpenID Connect credentials from
  @BotFather → your bot → **Login Widget**. Required outside Development, where the app
  refuses to start without them. In Development they are optional: the app starts with a
  warning and Telegram sign-in fails, but the local dev sign-in below still works.
* `Translation:MyMemoryEmail` - optional. Auto-translation for the personal dictionary uses
  MyMemory (api.mymemory.translated.net), which needs no key; the email only raises the daily
  quota. Leave it empty to run anonymously.

For a local run, fill in `LanguageLab.Api/appsettings.Development.json` (see the example
below). In Docker the same values are passed via env vars using the standard
ASP.NET Core convention (`__` instead of `:`): `ConnectionStrings__DefaultConnection`,
`Telegram__ClientId`, `Telegram__ClientSecret`.

### Accounts

Sign-in is Telegram over OpenID Connect ([docs](https://core.telegram.org/bots/telegram-login)) —
there is no password. The browser goes to `/api/auth/telegram/start`, authorises at
`oauth.telegram.org`, and comes back to `/api/auth/telegram/callback`, where the app exchanges
the code, validates the `id_token` and issues its own `ll_session` cookie. The cookie holds an
internal user id and a role, never Telegram's claims.

The first person to sign in successfully becomes the administrator; everyone after them is a
regular user. Regular users can use the dictionaries an admin made public but cannot import,
delete or re-publish one. Admins additionally get **Users** in the account menu, where they can
promote, ban and delete accounts. A ban takes effect on the banned user's next request, not at
their next login. Anyone can delete their own account from the account menu
(`DELETE /api/auth/me`) — a hard delete that takes their shelves and progress with it, while
dictionaries they imported stay behind without an owner. The one exception is the last
administrator, who is refused until someone else has been promoted.

**First deploy.** The migration leaves every existing account at the regular role, so right
after a fresh deploy the instance has zero admins — the first person to sign in becomes one.
Since the app is reachable at a public domain, sign in yourself immediately after the first
deploy, before sharing the URL, or promote yourself by hand:
`UPDATE "Users" SET "Role" = 1 WHERE "TelegramUserId" = <your id>;`

**Before the first deploy of this change**, check for duplicate Telegram ids, since the new
unique index on `Users.TelegramUserId` will fail to build if any exist — the old
single-config-user code had a check-then-insert race, and the database was historically also
written by a separate bot process:
```sql
SELECT "TelegramUserId", count(*) FROM "Users" GROUP BY 1 HAVING count(*) > 1;
```

**Allowed URLs.** In the same @BotFather *Login Widget* section, register every callback the
app is reached through — the flow is refused for any URL that is not listed:

```
https://l.kodzuverse.com/api/auth/telegram/callback
http://localhost:5173/api/auth/telegram/callback
```

Development can use this same real flow — the Vite dev server proxies `/api` without rewriting
`Host`, so the callback URL the app builds is the `localhost:5173` one registered above — or it
can skip it entirely with the local sign-in below.

**Local sign-in (development only).** The login screen shows a second button, **Sign in as
local dev**, which hits `GET /api/auth/dev-login` and signs you in as a dedicated account
(Telegram id `1`, "Local Developer") without touching Telegram. On an empty database that
account is created on the spot and, by the first-login rule, becomes the administrator — so a
fresh checkout needs neither @BotFather credentials nor a real Telegram account. It is not a
bypass of authorisation, only of the handshake: the session it issues is an ordinary one, and
a ban applies to it like any other.

Three independent fences keep it out of production, and each holds on its own:

1. `LanguageLab.Api/Auth/DevLogin.cs` and the endpoint that uses it are inside `#if DEBUG`, and
   the image publishes `-c Release` — the code is not in the deployed binary, so no
   environment variable can switch it back on.
2. The endpoint is mapped only when `IsDevelopment()`, which covers a Debug build pointed at a
   real database. Containers default to Production; `compose.yaml` sets no `ASPNETCORE_ENVIRONMENT`.
3. The button sits behind `import.meta.env.DEV`, which Vite folds to a literal `false` in
   `npm run build`.

Weakening any one of them is a security change, not a cleanup.

**Production.** The app sits behind a TLS-terminating proxy at `l.kodzuverse.com` and reads
`X-Forwarded-Proto`. Without that it would consider the request plain HTTP, refuse to issue the
`Secure` session cookie, and build an `http://` redirect URI that does not match the registered
Allowed URL.

The reverse proxy must also forward the original `Host` header (e.g. nginx's
`proxy_set_header Host $host;`) — the OIDC handler builds its `redirect_uri` from
`Request.Host`, and `ForwardedHeaders.XForwardedHost` is not enabled, so a proxy that
rewrites `Host` to an internal container name breaks every login with a `redirect_uri`
mismatch.

When registering the bot's OpenID Connect credentials in @BotFather's Login Widget section,
keep the default signing algorithm (RS256). Telegram's own docs state that the Web3-oriented
algorithms (ES256K, EdDSA) reject the `profile` scope — since the app requests `openid profile`
and the numeric Telegram id arrives via the `profile` scope's `id` claim, picking one of those
algorithms would break sign-in in a way that looks identical to a claim-mapping bug.

Telegram also caps the OAuth `state` parameter at 256 characters and answers `state too long`
before the consent screen ever renders. The OIDC handler's default format protects the whole
`AuthenticationProperties` — return url, correlation id, PKCE verifier — into roughly 410, so
`ServerSideStateFormat` (`LanguageLab.Api/Auth/`) keeps them in process memory and sends only a
43-character handle. That store is per-process: restarting the API mid-login costs the user a
retry.

### Personal dictionary and translation

Every user has a private "My words" dictionary (created on first use, `IsPersonal`). Words are
typed in by hand; `GET /api/translate?word=` suggests a Ukrainian translation — the shared
vocabulary first, MyMemory after — and the user edits it before adding. Added words are
`WordPair` rows owned by the user (`OwnerId`), so a personal translation never overwrites the
shared one, and they are shelved "don't know" at once so the usual exercise and review flows
train them like any book. Provider results are not cached (see TODO).

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
  "Telegram": {
    "ClientId": "{your bot's OpenID Connect client id}",
    "ClientSecret": "{your bot's OpenID Connect client secret}"
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
