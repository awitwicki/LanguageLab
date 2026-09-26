# LanguageLab

Web app for learning new words from books

1. Pick dictionary
2. learn new words
3. GOTO 1

## Documentation

- [docs/architecture.md](docs/architecture.md) — the project map: what each project and service
  owns, the endpoint inventory, the Postgres and config setup.
- [docs/auth.md](docs/auth.md) — Telegram sign-in over OIDC, the `ll_session` cookie, dev-login,
  the Mini App, roles.
- [docs/vocabulary-and-training.md](docs/vocabulary-and-training.md) — import and publication
  review, the personal dictionary, translation providers and budgets, Leitner training.
- [docs/reader.md](docs/reader.md) — Reading mode: what stays in the browser, what the server
  keeps, the word panel, fb2 and epub parsing.
- [docs/trainers.md](docs/trainers.md) — the irregular-verbs and pronunciation trainers.
- [web/README.md](web/README.md) — the SPA up close.
- [CLAUDE.md](CLAUDE.md) — the conventions and invariants the code is held to.

## TODO

Backlog of short topics. When something gets deferred (a stub, an inactive button, "we'll do it later") —
add one line here **in the same set of changes**. Done items are marked `[x]`.

- [ ] Resume an unfinished training session after a page reload (the session exists in the DB, no UI entry point yet)
- [ ] Move a word back from "know" to "don't know" outside the exercise-start screen: the cross-out in the batch preview (`web/src/training/useBatchPreview.ts`) — "bring back" only works within the current visit; after that the word can only be reached via `POST /api/sorting/mark`
- [ ] `ChapterStatsService.GetChapterViewsAsync`: one COUNT query per returned chapter (plus 3 whole-book queries per call) — also backs `GET /api/chapters/starred` on the home screen now, not just `GET /api/dictionaries/{id}`; merge into one GROUP BY if this ever becomes slow
- [ ] `RecentActivityService.GetSortingAsync` counts each listed scope separately — two COUNTs per row, up to `MaxRows` rows plus the fully-sorted ones it skips; merge into one GROUP BY with `ChapterStatsService.GetChapterViewsAsync` if the home screen ever feels slow
- [ ] Edit a personal word's translation after it was added (`web/src/screens/PersonalDictionaryScreen.tsx`, `PersonalDictionaryService`)
- [ ] `ImportScreen.test.tsx`'s two worker-parse previews ("parses the chosen book into a chapter preview", "parses a chosen epub into a chapter preview") fail about one full-suite run in three, and pass every time on their own — a timing race between the module worker and the assertion, not a product bug (`web/src/screens/ImportScreen.test.tsx`, `web/src/worker/parseBook.worker.ts`)
- [ ] Book import inside the Telegram Mini App: the lemmatizing worker never starts there (the screen sat at "Starting the word extractor…" with no progress and no error event), while the same build works in a browser. `ImportScreen` now shows a "use a browser" notice instead of the file picker when `telegramInitData()` is set; find out why the module worker does not run in Telegram's webview and bring the import back there (`web/src/screens/ImportScreen.tsx`, `web/src/worker/parseBook.worker.ts`)
- [ ] Reader word panel: undo a word that already has Leitner progress — the marked button is disabled for it (`canReset` from `ReaderWordService.GetAsync`, refused by `ResetAsync`), because dropping the shelf would have to decide what happens to the `WordProgress` row; the word can still be moved to another shelf, just not back to "new"
- [ ] Admin review of machine translations — `WordPair.TranslationOrigin = Machine` rows written by `TranslationService`
- [ ] Reader: sentence positions and cached translation keys depend on `Intl.Segmenter`'s exact output, which can differ across browser engines/ICU versions — resume is best-effort at the sentence level; chapter/paragraph indices are stable and `clampPosition` (`web/src/reader/readerBook.ts`) prevents a crash either way
- [ ] epub: split one XHTML document into chapters by the TOC's `#id` fragments — chapters follow the spine, so a single-document epub is one chapter, now cut into parts by length alone (`splitLongChapters`) rather than where the book says its chapters are (`web/src/books/epub.ts`, `tocTitles` already resolves fragments away)
- [ ] epub: images, tables and footnote panes are not shown — a footnote's text is dropped with its `epub:type` element, and `<table>` contributes only what its cells hold as paragraphs (`web/src/books/epub.ts`)
- [ ] User-facing reports on a published dictionary ("this is spam") feeding the same admin queue — `DictionaryPublicationService`
- [ ] A one-off audit of shared `WordPair` rows that predate the import word rule — nothing cleans up what is already in the table
- [ ] Moderation queue: flag when approving a dictionary would make it the lowest-id match for a `FileHash` an existing `ReaderBook` already points elsewhere — nothing surfaces that collision to the admin today (`DictionaryPublicationService`, `ReaderBookService`)
- [ ] Grammar mode (MVP, about five minutes a day): a fifth top-level mode (`AppMode`, `web/src/layout/mode.ts`) with one or two of the simplest topics, each with an interactive drill. A domain of its own like the irregular verbs — topics and exercises in code, no `WordPair` and no dictionary
- [ ] Phrasal verbs and idioms: a sixth top-level mode (`AppMode`, `web/src/layout/mode.ts`) for multi-word vocabulary — phrasal verbs (`give up`, `knuckle down`, `copy in`, `get along with`) and idioms (`a piece of cake`, `word of mouth`, `be up in the air`, `nip it in the bud`) as two groups of one catalog, since a learner's list of them is always mixed. A domain of its own like the irregular verbs — the phrases and their exercises in code, no dictionary; a book import cannot reach them either way (`ImportWordText` takes a single lowercase word), though `WordText` already allows spaces, so the personal dictionary can hold a phrase today
- [ ] Multilingual: the learner picks a main language when the account is created (Telegram's `language_code` from the launch parameters is the default to offer), stored on `TelegramUser`, and every translator works English → that language instead of English → Ukrainian. The fixed pair sits in `MyMemoryTranslator` and `MyMemorySentenceTranslator` (`langpair=en|uk`), `DeepLTranslator`'s `target_lang`, and the `ITranslator`/`ISentenceTranslator` contracts. The harder half is storage: a shared `WordPair` row is unique on `(Word, OwnerId)` and holds one `Translation`, with no room for a second language — the shared vocabulary needs a language of its own (a row per language, or a translations table beside it), and the same goes for what the per-language cache of `TranslationService.LookupAsync` may reuse. `IrregularVerbCatalog`'s translations are Ukrainian in code too
- [ ] Auto-translate on book import and when marking a word "don't know" + edit translation in the UI — `TranslationService.LookupAsync` (`LanguageLab.Application/Translation`) now caches a provider's answer into the shared vocabulary on every lookup, but nothing calls it from `BookImportService` or `WordSortingService.MarkAsync` yet (one-off backfill of 2797 "don't know" shelf words done on 2026-09-07; new "don't know" words without a translation still don't enter exercises)

## Development

### Conventions

Everything in the project — code, comments, docs, UI copy — is English. The one exception: the vocabulary translation shown to the learner (`WordPair.Translation`, the Ukrainian meaning of each English word) stays Ukrainian, since that's the point of the app. See `CLAUDE.md` → Frontend conventions.

### Versioning

The version lives in one place: the `<Version>` element of `Directory.Build.props` at the repo
root. MSBuild applies it to every project (assembly, file and informational version of the API,
the bot and the tests), and `web/vite.config.ts` reads the same element at build time and bakes
it into the SPA (`src/lib/version.ts`), which shows it next to the logo in the top bar. Bump it
there by hand, following the commit subjects: a `feat` commit raises the minor part and resets
the patch, a `fix` or `perf` raises the patch, anything else leaves it alone; the major part
stays 0 while the app is pre-release. `dotnet build /p:Version=…` would override only the .NET
side — the file is the contract, so edit the file.

CI (TeamCity) turns `0.0.0` into `0.0.0.N`, `N` being the build counter, with the **File
Content Replacer** build feature, which patches the file before the first step and restores it
after the build (nothing is ever committed):

| Field | Value |
|---|---|
| Process files | `Directory.Build.props` |
| Find what | `<Version>(\d+\.\d+\.\d+)</Version>` |
| Regex mode | Regex, match case |
| Replace with | `<Version>$1.%build.counter%</Version>` |

`%build.counter%` rather than `%build.number%`: the counter is always a bare integer, so the
result does not depend on the configuration's build number format. Both Dockerfiles copy the
patched file into their build stages, so the assemblies and the SPA pick it up. To make
TeamCity itself show `0.0.0.N` as the build number, add a Command Line step in front of the
`docker compose build` — the replacer has already run by then:

```bash
echo "##teamcity[buildNumber '$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)']"
```

### Docker / .env

`compose.yaml` brings up `LanguageLab.Api` and the Telegram bot `LanguageLab.TgBot` (Postgres is
commented out there; see Run below). Both read `.env`, with keys in the ASP.NET Core `__`
spelling:

* `POSTGRES_PASSWORD={password}` - Postgres password, referenced by `compose.yaml` for the
  database container and the API's connection string
* `Telegram__ClientId` / `Telegram__ClientSecret` - the API's OpenID Connect credentials (see
  Accounts, below)
* `Telegram__BotToken` - the bot token from @BotFather. The bot talks to Telegram with it, and
  the API validates Mini App sign-ins with it (see Accounts → Inside Telegram)
* `WebApp__Url` - the public `https://` address of the app, which the bot's button opens
* `Translation__MyMemoryEmail` - optional; any contact email raises MyMemory's free
  translation quota from 5 000 to 50 000 characters a day per server IP (see Translation, below).
  Recommended in production: without a DeepL key, sentences get 40 % of that quota for
  themselves (2 000 characters a day without this email, 20 000 with it)
* `Translation__DeepLApiKey` - optional; a DeepL API Free key for sentence translation in the
  reader. Without it MyMemory translates sentences as well.

**Docker compose:** create `.env` file and fill it with those variables.

### LanguageLab.Api

`LanguageLab.Api` reads its config from `appsettings.json` / `appsettings.Development.json`
(the latter is local, gitignored), not from env vars:

* `ConnectionStrings:DefaultConnection` - postgres connection string
* `Telegram:ClientId` / `Telegram:ClientSecret` - OpenID Connect credentials from
  @BotFather → your bot → **Login Widget**. Required outside Development, where the app
  refuses to start without them. In Development they are optional: the app starts with a
  warning and Telegram sign-in fails, but the local dev sign-in below still works.
* `Telegram:BotToken` - the bot token from @BotFather. The Mini App sign-in
  (`POST /api/auth/telegram/webapp`) checks Telegram's launch parameters against it. Required
  outside Development, like the credentials above; in Development it is optional, and without
  it signing in from inside Telegram answers 503.
* `Translation:MyMemoryEmail` - optional. Auto-translation for the personal dictionary uses
  MyMemory (api.mymemory.translated.net), which needs no key; the email only raises the daily
  quota. Leave it empty to run anonymously. When MyMemory also translates sentences (no DeepL
  key), sentences get 40 % of that daily quota for themselves: 2 000 characters without this
  email, 20 000 with it — recommended in production so the reader does not exhaust the day's
  quota for everyone's word lookups.
* `Translation:DeepLApiKey` - optional. A DeepL API Free key (ends in `:fx`), for the reader's
  sentence translation — 500 000 characters a month for the whole app, 20 000 characters a day
  per user. Without it MyMemory translates sentences as well.

For a local run, fill in `LanguageLab.Api/appsettings.Development.json` (see the example
below). In Docker the same values are passed via env vars using the standard
ASP.NET Core convention (`__` instead of `:`): `ConnectionStrings__DefaultConnection`,
`Telegram__ClientId`, `Telegram__ClientSecret`, `Telegram__BotToken`.

### LanguageLab.TgBot

A `/start`-only bot: it answers every private message with the bot's name, a line on what the
app does and an **Open LanguageLab** button that opens the app inside Telegram, and it sets the
chat's menu button to the same page. It long-polls, keeps no state and never touches the
database — the app itself signs the user in (see Accounts → Inside Telegram). Configuration:

* `Telegram:BotToken` - the bot token from @BotFather; required
* `WebApp:Url` - the public address the button opens; required, and must be `https://`, since
  Telegram does not open a Mini App over plain http

Locally either export them (`Telegram__BotToken=… WebApp__Url=… dotnet run --project
LanguageLab.TgBot`) or put them in `LanguageLab.TgBot/appsettings.Development.json` (gitignored)
and run with `DOTNET_ENVIRONMENT=Development`. A local bot has to point at an `https://` app —
the deployed one, or a tunnel — so the usual local loop is the API plus Vite, without the bot.

### Accounts

Sign-in is Telegram over OpenID Connect ([docs](https://core.telegram.org/bots/telegram-login)) —
there is no password. The browser goes to `/api/auth/telegram/start`, authorises at
`oauth.telegram.org`, and comes back to `/api/auth/telegram/callback`, where the app exchanges
the code, validates the `id_token` and issues its own `ll_session` cookie. The cookie holds an
internal user id and a role, never Telegram's claims.

The first person to sign in successfully becomes the administrator; everyone after them is a
regular user. Importing a book takes no role at all — every signed-in user may, and what they
import stays private until an admin approves it from the moderation queue. Only an admin's own
import is published without review, and deleting or re-publishing a dictionary stays with
admins. There are just the two roles: an admin picks each account's (user, admin) from
**Users** in the account menu, and bans or deletes accounts there. A ban takes effect on
the banned user's next request, not at their next login. Anyone can delete their own account from the account menu
(`DELETE /api/auth/me`) — a hard delete that takes their shelves and progress with it, while
dictionaries they imported stay behind without an owner. The one exception is the last
administrator, who is refused until someone else has been promoted.

**Dropping the uploader role.** The `DropUploaderRole` migration moves any account still on the
old uploader role back to the regular one. Their session cookie still spells the removed role,
which no longer parses, so those accounts are signed out on their next request and simply sign
in again — nothing they own is touched.

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

**Inside Telegram (Mini App).** The bot's **Open LanguageLab** button (and the chat's menu
button) opens the app in Telegram's own web view. Telegram hands that page signed launch
parameters — `window.Telegram.WebApp.initData`, a query string whose `hash` is an HMAC-SHA256
over the other fields keyed by the bot token — and the SPA posts them to
`POST /api/auth/telegram/webapp`. The server recomputes the hash
(`LanguageLab.Api/Auth/WebAppInitData.cs`), refuses launches older than 24 hours, and then runs
the same login as the OIDC callback: first login registers, the first account is the admin, a
ban answers 403, and the session is the same `ll_session` cookie. There is no login screen on
that path; the OIDC flow is only for a browser.

At boot the SPA asks `/api/auth/me` first and posts the launch parameters only on a 401, so a
web view kept open past the 24-hour window keeps working on its cookie. If the sign-in is
refused, the login screen shows a **Sign in with Telegram** button that retries it, and a
message asking to reopen the app from the bot.

Telegram Web (web.telegram.org in a desktop browser) opens Mini Apps in an iframe, and the
`SameSite=Lax` session cookie is not sent from a cross-site iframe — so that client does not
work; iOS, Android and Desktop do. Supporting it would mean `SameSite=None` on the cookie.

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

Brings up `LanguageLab.Api` (which also serves the web app at `http://localhost:5080`) and the
Telegram bot in one move; Postgres is commented out in `compose.yaml` and runs on its own.

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
    "ClientSecret": "{your bot's OpenID Connect client secret}",
    "BotToken": "{your bot token}"
  }
}
```

```bash
dotnet run --project LanguageLab.Api
```

Comes up at `http://localhost:5080`.

**Bot** (optional, separate terminal) — see Development → LanguageLab.TgBot for its two
settings; it needs an `https://` app URL to point at.

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
