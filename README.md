# LanguageLab

Telegram bot for learn new words

1. Pick dictionary
2. learn new words
3. GOTO 1

## Development

### Prepare python environment

```bash
pip install uv
uv init
uv sync
uv pip install -r requirements.txt
uv run extract.py 
```

`extract.py` - extract words in base from fb2 file and save to txt file

### TODO:
* [ ] .fb2 Words extractor (extract words in base form, translate and export)
* [ ] docker compose file
* [ ] Telegram bot -> dictionary list
* [ ] Telegram bot -> procedural exercises (pick dict, learn new 20 words)
* [ ] Telegram bot -> procedural exercises (test yourself, if not ok - relearn)
* [ ] Telegram bot -> procedural exercises (learn next batch of words from dictionary)
* [ ] Telegram bot -> pick new dict, learn new words except for already learned
* [ ] Telegram bot -> my stats
* [ ] Database with migrations for telegram bot

