# LanguageLab

Web app for learning new words from books. Users pick a dictionary extracted from a book, then learn words through procedural exercises in the SPA.

## Rules for Claude agents

- **Never commit or push** until the user explicitly asks. Show the diff, wait for approval.
- Do not run `git add`, `git commit`, `git push`, or any history-rewriting command on your own.
- Коли юзер дозволив комітити по кроках багатокрокової роботи (наприклад, виконання плану — один коміт на таск), по завершенні всіх кроків зведи ці коміти в один (`git reset --soft` до стану перед першим із них і закомміть заново одним комітом) **перед тим, як вважати роботу зробленою**. Юзер рев'ює перед пушем один коміт, а не серію. Це squash проміжних комітів у межах уже дозволеної роботи, не самостійний дозвіл на новий коміт — але сам push усе одно чекає на окреме прохання.
- Міграції створювати можна (`dotnet ef ... migrations add`), але `--startup-project` тепер `LanguageLab.Api`.

## Project layout

- `LanguageLab.Domain/` — entities (`Dictionary`, `WordPair`, `KnownWord`, `UnknownWord`, `TelegramUser`, `Training`, `TrainingEvent`) and interfaces. No dependencies on infrastructure.
- `LanguageLab.Infrastructure/` — EF Core `ApplicationDbContext`, PostgreSQL provider, migrations.
- `LanguageLab.Application/` — сервіси поверх домену: вибірка слів, сесії тренування, імпорт книжок, сортування. Використовуються API.
- `LanguageLab.Api/` — ASP.NET Core Minimal API + роздача SPA. Веде міграції БД.
- `web/` — React + Vite SPA: імпорт fb2 у браузері, статистика словника, сортування слів. `src/layout/` (оболонка: топбар + сайдбар), `src/screens/`, `src/components/`, `src/lib/` (форматери), тести `*.test.ts(x)` поруч із кодом (vitest + jsdom, хелпер `src/test/render.ts`). Деталі — у [web/README.md](web/README.md).
- `extract.py` — Python/spaCy pipeline that pulls base-form words from `.fb2` books into dictionaries under `dictionaries/`.

## Runtime

- Postgres via `compose.yaml`. `LanguageLab.Api` reads `ConnectionStrings:DefaultConnection` and `WebUser:TelegramId` from `appsettings.json`/`appsettings.Development.json` (the latter is gitignored, local only); in Docker the same keys come from env vars via the `__` convention (`ConnectionStrings__DefaultConnection`, `WebUser__TelegramId`).
- Migrations run automatically on startup (`dbContext.Database.MigrateAsync()` in [Program.cs:39](LanguageLab.Api/Program.cs#L39)).

## Adding a migration

```
dotnet ef --project LanguageLab.Infrastructure --startup-project LanguageLab.Api migrations add <Name>
```

## TODO-беклог

`README.md` має секцію `## TODO` — беклог коротких тем. Правила:

- Відклав функціонал (заглушка, кнопка без дії, «зробимо пізніше») — додай один рядок туди **в тому ж наборі змін**. Спершу перевір, чи такого пункту ще немає.
- Юзер каже «зроби щось із туду» / «візьми туду» — бери пункт звідти (найперший незакритий, якщо не вказано інший), після виконання став `[x]`.
- Пункти — короткі, з місцем у коді, де це має жити, якщо воно вже відомо.

## Frontend conventions (`web/`)

- Дизайн-токени (кольори, типографіка, радіуси, рух) — тільки в `web/src/index.css`; у CSS компонентів — лише `var(--…)`, жодних hex. Світла/темна тема — через `prefers-color-scheme`.
- CSS лежить поруч із компонентом і імпортується з його `.tsx`. Нові кнопки — через `.btn` + `.btn-primary | .btn-secondary | .btn-quiet` (+ `.btn-lg`).
- Копі — українська, sentence case, дієслова на кнопках. Гарячі клавіші показуються окремим `<kbd>`, не в тексті.
- Числа — з `formatInt`/`wordsLabel` із `web/src/lib/format.ts`, клас `.num` для табличних цифр.
- Роутинг — in-memory `Route` у `App.tsx`; без react-router. Нових npm-залежностей не додавати без запиту юзера.
- Перевірка перед здачею: `cd web && npm run lint && npm test && npm run build`.

## Python pipeline

Managed with `uv`. Requires the `en_core_web_sm` spaCy model — see [README.md](README.md#python-environment) for the install line.
