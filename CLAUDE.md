# LanguageLab

Telegram bot for learning new words from books. Users pick a dictionary extracted from a book, then learn words through procedural exercises.

## Rules for Claude agents

- **Never commit or push** until the user explicitly asks. Show the diff, wait for approval.
- Do not run `git add`, `git commit`, `git push`, or any history-rewriting command on your own.
- Do not create migrations automatically — surface the entity change and let the user decide when to run `dotnet ef migrations add`.

## Project layout

- `LanguageLab.Domain/` — entities (`Dictionary`, `WordPair`, `KnownWord`, `UnknownWord`, `TelegramUser`, `Training`, `TrainingEvent`) and interfaces. No dependencies on infrastructure.
- `LanguageLab.Infrastructure/` — EF Core `ApplicationDbContext`, PostgreSQL provider, migrations.
- `LanguageLab.TgBot/` — entry point ([Program.cs](LanguageLab.TgBot/Program.cs)), handlers, services. Uses PowerBot.Lite + Autofac for DI, NLog for logging.
- `extract.py` — Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under `dictionaries/`.

## Runtime

- Postgres via `compose.yaml`. App reads `TELEGRAM_TOKEN`, `DB_CONNECTION_STRING`, optional `MODERATORS_LIST` from env.
- Migrations run automatically on startup (`dbContext.Database.MigrateAsync()` in [Program.cs:33](LanguageLab.TgBot/Program.cs#L33)).

## Adding a migration

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.TgBot migrations add <Name>
```

## Python pipeline

Managed with `uv`. Requires the `en_core_web_sm` spaCy model — see [README.md](README.md#python-environment) for the install line.
