# LanguageLab

Web app for learning new words from books

1. Pick dictionary
2. learn new words
3. GOTO 1

## TODO

Беклог коротких тем. Коли щось відкладається (заглушка, кнопка без дії, «зробимо пізніше») —
сюди додається один рядок у тому ж наборі змін. Виконане позначається `[x]`.

- [ ] Меню акаунта в топбарі: зараз кнопка без дії (профіль, вихід)
- [ ] «Почати вправу» на екрані словника: запуск Leitner-тренування у вебі (`TrainingSessionService` уже є в `LanguageLab.Application`)
- [ ] Видалення словника з UI (`DELETE /api/dictionaries/{id}` уже є)
- [ ] Переклади слів: переклад і показ на картці сортування (`WordPair.Translation` поки порожній)
- [ ] Статистика юзера: скільки знаю / вчу / виключено загалом
- [ ] Колірний контраст WCAG AA: перевірити `.btn-known`/`.btn-unknown` у світлій темі й `.btn-primary` на `--accent` у темній
- [ ] Фокус-менеджмент при зміні маршруту: анонс нового екрана для скрінрідерів (зараз фокус падає на `<body>`)
- [ ] Spacing-токени в design system: `web/src/index.css` токенізує колір/типографіку/радіуси, але не відступи
- [x] .fb2 words extractor у браузері (імпорт книжки з главами)
- [x] Docker compose, міграції БД
- [x] Сортування слів «знаю / не знаю / виключити» по книжці або главі

## Development

### Docker / .env

`compose.yaml` піднімає Postgres і `LanguageLab.Api` разом. Env змінні:

* `POSTGRES_PASSWORD={password}` - Postgres password, referenced by `compose.yaml` for both the database container and the API's connection string

**Docker compose:**  create `.env` file and fill it with that variable.

### LanguageLab.Api

`LanguageLab.Api` бере конфіг з `appsettings.json` / `appsettings.Development.json`
(другий — локальний, у git не потрапляє), а не з env змінних:

* `ConnectionStrings:DefaultConnection` - postgres connection string
* `WebUser:TelegramId` - користувач, від імені якого працює веб (авторизації поки немає)

Для локального запуску заповни `LanguageLab.Api/appsettings.Development.json` (див. приклад
нижче). У Docker ті самі значення передаються через env змінні за стандартною
ASP.NET Core конвенцією (`__` замість `:`): `ConnectionStrings__DefaultConnection`,
`WebUser__TelegramId`.

## Run

### З Docker (весь стек)

```
docker-compose up --build -d
```

Піднімає Postgres і `LanguageLab.Api` (роздає й веб-застосунок на `http://localhost:5080`)
одним рухом.

### Локально, без Docker (dotnet + npm)

Потрібен лише запущений Postgres — найпростіше підняти саме його контейнером
(решта процесів далі йдуть напряму на хості):

```bash
docker compose up -d dbpostgres
```

(або вкажи власний локальний Postgres — головне, щоб `ConnectionStrings:DefaultConnection`
у `appsettings.Development.json` вказував на нього; порт `5433` — це те, що compose
мапить назовні з контейнера).

**API** (окремий термінал; він же мігрує схему БД на старті й роздає веб-застосунок,
якщо `web/` зібраний — у дев-режимі краще користуватись Vite нижче).

Спершу заповни `LanguageLab.Api/appsettings.Development.json` (файл локальний,
у git не потрапляє):

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

Підніметься на `http://localhost:5080`.

**Веб-застосунок** (окремий термінал; Vite з проксі на API, потрібен для розробки фронтенду):

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
