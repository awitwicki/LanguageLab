# LanguageLab — web

React 19 + TypeScript + Vite. SPA роздається `LanguageLab.Api`; у розробці — Vite з проксі `/api` → `http://localhost:5080`.

```bash
npm install
npm run dev      # http://localhost:5173
npm test         # vitest (jsdom)
npm run lint     # oxlint
npm run build    # tsc -b && vite build → dist/
```

## Структура

- `src/layout/` — `AppShell` (топбар + сайдбар + контент), `TopBar`, `Sidebar`.
- `src/screens/` — екрани: `HomeScreen`, `ImportScreen`, `DictionaryScreen`, `SortingScreen`.
- `src/components/` — `ProgressBar`, `SortingProgress`.
- `src/lib/` — форматери чисел і підписів (`format.ts`, `labels.ts`).
- `src/fb2/`, `src/worker/` — розбір fb2 і лематизація в браузері.
- `src/sorting/useSortingQueue.ts` — буфер черги сортування з оптимістичними позначками.
- `src/api/client.ts` — типи відповідей API і fetch-обгортки.

## Стилі

Токени (кольори, типографіка, радіуси, рух) — у `src/index.css`; там же базові класи `.btn*`, `.large-title`, `.title`, `.footnote`, `.num`. CSS кожного компонента лежить поруч із ним і імпортується з `.tsx`. Нових кольорів поза токенами не додаємо; теми — через `prefers-color-scheme`.

## Тести

`src/test/render.ts` — мінімальний рендер на `react-dom/client` + `act` (без testing-library): `render`, `flush`, `click`. Тести лежать поруч із кодом: `*.test.ts(x)`.
