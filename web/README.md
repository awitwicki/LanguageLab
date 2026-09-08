# LanguageLab — web

React 19 + TypeScript + Vite. The SPA is served by `LanguageLab.Api`; in development — Vite with a `/api` → `http://localhost:5080` proxy.

```bash
npm install
npm run dev      # http://localhost:5173
npm test         # vitest (jsdom)
npm run lint     # oxlint
npm run build    # tsc -b && vite build → dist/
```

## Structure

- `src/layout/` — `AppShell` (top bar + sidebar + content), `TopBar`, `Sidebar`.
- `src/screens/` — screens: `HomeScreen`, `ImportScreen`, `DictionaryScreen`, `SortingScreen`, `TrainingStartScreen` (batch size), `TrainingScreen` (cards → quiz → summary).
- `src/components/` — `ProgressBar`, `SortingProgress`, `LeitnerScale` (Leitner box scale + weighted percent).
- `src/lib/` — number and label formatters (`format.ts`, `labels.ts`).
- `src/fb2/`, `src/worker/` — fb2 parsing and lemmatization in the browser.
- `src/sorting/useSortingQueue.ts` — sorting-queue buffer with optimistic marks.
- `src/training/useBatchPreview.ts` — batch preview for the start screen: candidates by frequency, "know" cross-out / "bring back", pure `reconcileRows`.
- `src/api/client.ts` — API response types and fetch wrappers.

## Styles

Tokens (colors, typography, radii, motion) — in `src/index.css`; same place has the base classes `.btn*`, `.large-title`, `.title`, `.footnote`, `.num`. Each component's CSS lives next to it and is imported from its `.tsx`. No new colors outside the tokens; themes — via `prefers-color-scheme`.

## Tests

`src/test/render.ts` — a minimal renderer on `react-dom/client` + `act` (no testing-library): `render`, `flush`, `click`. Tests live next to the code: `*.test.ts(x)`.
