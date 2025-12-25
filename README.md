# LanguageLab

Telegram bot for learn new words

1. Pick dictionary
2. learn new words
3. GOTO 1

### TODO:
* [x] .fb2 Words extractor (extract words in base form and export)
* [ ] english words translator and export
* [x] docker compose file
* [x] Database with migrations for telegram bot
* [ ] Telegram bot -> dictionary list
* [ ] Telegram bot -> procedural exercises (pick dict, learn new 20 words)
* [ ] Telegram bot -> procedural exercises (test yourself, if not ok - relearn)
* [ ] Telegram bot -> procedural exercises (learn next batch of words from dictionary)
* [ ] Telegram bot -> pick new dict, learn new words except for already learned
* [ ] Telegram bot -> my stats

## Development

### Telegram bot

Use next environment variables:

* `TELEGRAM_TOKEN={your token}` - telegram token
* `DB_CONNECTION_STRING={connection string}` - postgres connection string for entity framework
* `MODERATORS_LIST={userid1,userid2,...}` - (optional) list of telegram users id allowed to upload dictionaries

**Docker compose:**  create `.env` file and fill it with that variables.

## Run

```
docker-compose up --build -d
```

Postgresql database is required.

### Database migrations

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.TgBot migrations add {migrationName}
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


