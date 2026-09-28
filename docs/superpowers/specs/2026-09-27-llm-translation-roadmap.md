# LLM translation and server-side import — roadmap

Umbrella tracker for the work that replaces MyMemory/DeepL with an LLM translator and moves book
import onto the server. Each workstream below still goes through its own spec → plan →
implementation cycle; this file holds the decomposition, the decisions every workstream must
respect, the contracts that let them run in parallel, and progress.

**Status legend:** `[ ]` not started · `[~]` in progress (note the branch/worktree) · `[x]` done.

## Goal

1. Book import: the browser uploads the book file; the server hashes it, parses it, lemmatizes it
   against an English lexicon (a form not in the lexicon is dropped — no more `aargh`), and
   creates the dictionary. The hash is the server's own, so nobody can file a dictionary under a
   book they do not have.
2. Translation: one LLM provider interface with two implementations — Gemini (default) and an
   OpenAI-compatible client (DeepSeek as the alternative). MyMemory, DeepL and the
   `DailyCharacterBudget` 60/40 split are removed.
3. A dictionary's missing translations are filled in the background, per learner language, only
   for words the shared vocabulary does not already translate.
4. Reader translation of user-supplied text uses a hardened prompt: fixed system instruction, the
   text passed as data, a constrained JSON output schema, no secrets or tools in context.
5. Per-user limits: 1 book import per day; 1 uncached translation per 10 seconds (a cache hit is
   free).

## Decisions

- 2026-09-27 — Replace MyMemory and DeepL outright; no non-LLM fallback. DeepSeek (via an
  OpenAI-compatible client) is the second provider, selected by config.
- 2026-09-27 — Lemmatization on the server is a lookup table (form → lemma), which doubles as the
  "is this a real English word" whitelist.
- 2026-09-27 (Q1) — No server-side sentence cache. A sentence translation goes to the client only
  and is never stored or logged on the server; the device cache in `bookStore.ts` stays. Every
  sentence request reaches the LLM, so every one counts against the 1-per-10 s limit. Sentence
  input capped at ~500 characters (down from 1000).
- 2026-09-27 (Q1) — Words: a translation already in `WordTranslations` is answered at once and is
  not rate-limited; a miss goes to the LLM, is written to `WordTranslations`, and counts against
  the 1-per-10 s limit. A word lookup (the single word a reader taps, and `GET /api/translate`)
  is capped at 100 characters.
- 2026-09-27 (Q2) — One lexicon for server and client. The C1 form → lemma table is also served
  to the SPA as a static, browser-cached asset; the reader's highlights and word panel lemmatize
  with it, and `compromise` + `wink-lemmatizer` are removed. Context-free by design: an ambiguous
  form maps to the one lemma the table records for it.
- 2026-09-27 (Q3) — Lexicon source: SCOWL + AGID (Kevin Atkinson). Start at SCOWL size 60,
  without proper names and abbreviations; the level is a build-script parameter, so it can be
  raised to 70 later. The C1 spec confirms the licence and records it next to the data file.
- 2026-09-27 (Q4) — The reader's silent auto-import (`useAutoImport.ts`) goes away. On open the
  client still hashes the book and links a visible dictionary with that hash (a client hash is
  safe for a read-only link); with none, the reader offers a **Build dictionary** button that
  uploads the file through the import endpoint and counts against 1 import/day (disabled, with
  when it frees up, once spent). The server never stores the file — it parses and hashes it only;
  reader.md's "never uploaded" becomes "sent once, for import, never stored".
- 2026-09-27 (Q5) — Admins are exempt from 1 import/day. The 1 uncached translation per 10 s
  limit applies to everyone, admins included.
- 2026-09-27 (Q6) — A dictionary is queued for the importer's language on import, and for any
  other language on demand: when a user whose language is L opens the dictionary (or the reader
  opens a book linked to it) and it has words with no translation in L. The job is unique per
  (dictionary, language), so repeat opens are no-ops; the dictionary screen shows
  "Translating… N of M" while one runs.
- 2026-09-27 (Q7) — Config keys: `Translation:Provider` (`Gemini` | `OpenAiCompatible`),
  `Translation:Gemini:ApiKey|Model`, `Translation:OpenAi:BaseUrl|ApiKey|Model` (DeepSeek:
  `https://api.deepseek.com`, `deepseek-chat`). Gemini model: the cheapest fast class
  (Flash-Lite, Flash if quality is short); the A1 spec takes the exact id and free-tier quotas
  from Google's current docs, not from memory, and puts the default in code, overridable by
  config. No key → the translator is unconfigured: lookups answer "translation unavailable", the
  queue idles, the app still starts — in Production too, with a startup warning in the log.
- 2026-09-28 (amends Q7) — DeepSeek retired `deepseek-chat` on 2026-07-24; the OpenAI-compatible
  default is `deepseek-flash`. Gemini 2.x is closed to new integrations; the Gemini default is a
  GA 3.x Flash-Lite id, chosen in the A1 spec. Gemini 3.x thinks by default and thinking tokens
  count against `maxOutputTokens`, so the client sends the lowest thinking level.
- 2026-09-28 (A1) — `ILlmClient` guarantees only that the answer is a valid JSON object. Checking
  its shape against `LlmRequest.ResponseSchema` is the consumer's job (A2/A3 read specific fields
  anyway); Gemini enforces the schema natively, the OpenAI-compatible client passes it to the
  model as text in the system prompt. A1 amends `LlmRequest`'s doc-comment to say so.
- 2026-09-28 (C1) — Lexicon primary lemma: an inflection wins (`found → find`, `left → leave`),
  the form itself comes last when it is a lemma too; a hand-kept `scripts/lexicon-overrides.txt`
  fixes known exceptions (`lay`). `IEnglishLexicon` gains `LemmasOf` (all lemmas, primary first);
  `LemmaOf` stays as the primary.
- 2026-09-28 (C1 → C3, C4) — Two-letter lemmas the lexicon knows (`go`, `ox`) are importable:
  now that the lexicon is the junk filter, `ImportWordText`'s 3-letter minimum becomes 2 for a
  word that is a lexicon lemma. The stop-word filter still applies (`be`, `do` stay out). C3
  changes `ImportWordText` and the `CLAUDE.md` import invariant; C4 aligns the reader's
  `isRejected` length rule.
- 2026-09-28 (C1, implementation) — The lexicon reads SCOWL's `english-words` lists as well as
  `american-words`/`british-words`: the latter two hold only the variety-specific spellings.
  SCOWL's lists also carry inflected forms, so "the form itself, if it is a lemma" means: in the
  lists and either an AGID headword or no AGID inflection of one — `went → go`,
  `houses → house`, still `found → find found`. C3/C4 can rely on every lemma named in the
  file having a line of its own. AGID's `infl.txt` marker syntax differs from the spec's
  assumed table in two ways future streams should know: a variant level is space-separated from
  the word and may be a decimal (`dreamt 1`, `waked 0.1`; only the whole-number part is compared
  to the drop threshold), and `!` marks a form as likely an inflection of a *different* word and
  is dropped rather than kept (its README: "likely an inflection of a similar word ... and not
  the current word").
- 2026-09-28 (C1, final review) — AGID auto-generates a comparative/superlative for many obscure
  adjective headwords, and some of those generated forms coincidentally spell far more common,
  unrelated English words (`numb → number`, `inter → interest`); "an inflection wins" then buries
  the common word behind the obscure one. ~20 clear cases (`number`, `interest`, `morning`,
  `customer`, `priest`, …) are fixed via `scripts/lexicon-overrides.txt`; a broader signal (~679
  SCOWL-size-≤-20 forms listing themselves last behind another lemma) was not exhaustively
  audited. C3, when wiring `LemmaOf` into import, should expect occasional wrong primaries on
  size-≤-20 words derived from an `A`-POS or `?`-POS AGID line, and treat a lexicon-overrides.txt
  addition as the fix, not a code change.
- 2026-09-28 (B1 → A2) — `AddTranslationQueue()` registers `NullWordBatchTranslator` with
  `TryAddScoped` and `TimeProvider.System` with `TryAddSingleton`. A2 registers its
  `IWordBatchTranslator` with a plain `AddScoped` (or a typed `AddHttpClient`) in its own
  `Add…` method and wins in either order; a stream that needs a clock injects `TimeProvider`.
- 2026-09-29 (Q8, amends C3) — The 1-import/day quota now counts only a *successful* import, not
  every attempt: a wrong file, a non-English book or DRM is immediately retryable, not a ~24h
  lockout. C3's declarative `UserRateLimits.Import` (`SlidingWindowRateLimiter` middleware, which
  consumes its permit before the handler runs and cannot see the outcome) is replaced by C4 with an
  in-memory `ImportQuota` the service asks itself — same "a restart forgives everybody" style as
  `SentenceQuota` — recording success only after `BookImportService.ImportAsync` returns without
  throwing. `ReaderCapabilities` gains `ImportRetryAfterSeconds` so the reader's Build dictionary
  button can show its wait without a click. See `2026-09-29-llm-c4-frontend-import-design.md`.

No open questions remain at roadmap level; each workstream's spec settles its own details and
adds a decision here only when it changes something another workstream relies on.

## Contracts (fixed up front so workstreams can run in parallel)

Wave 0 lands these on `dev` as interfaces plus test fakes, nothing else, so every parallel branch
compiles against the same shapes. A change to a contract after Wave 0 is recorded here and in the
progress log.

```csharp
// LanguageLab.Application/Translation
public interface ILlmClient                       // A1 implements: Gemini, OpenAI-compatible
{
    bool IsConfigured { get; }
    // One call, JSON in/out against a schema; throws LlmQuotaException / LlmUnavailableException.
    Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken ct);
}

public interface IWordBatchTranslator            // A2 implements on top of ILlmClient
{
    bool IsConfigured { get; }
    // Missing keys = the model gave no usable translation for that word.
    Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<string> lemmas, LearnerLanguage target, CancellationToken ct);
}

// ISentenceTranslator — existing contract, A3 re-implements it on ILlmClient.

public interface ITranslationQueue                // B1 implements
{
    Task EnqueueAsync(long dictionaryId, LearnerLanguage language, CancellationToken ct);
}

// LanguageLab.Domain
public interface IEnglishLexicon                  // C1 implements: EnglishLexicon
{
    IReadOnlyList<string> LemmasOf(string lowercaseForm); // all lemmas, primary first; [] = not English
    string? LemmaOf(string lowercaseForm);        // the primary; null = not an English word → drop
}

// LanguageLab.Application/Books
public interface IBookParser                      // C2 implements (fb2 + epub)
{
    ParsedBook Parse(byte[] file);                // throws BookFormatException(Invalid|Encrypted)
}
public sealed record ParsedBook(string Title, string? Author, IReadOnlyList<BookSection> Sections, int MaxDepth);
public sealed record BookSection(string Title, int Depth, string OwnText, IReadOnlyList<BookSection> Children);
public readonly record struct ChapterMode(int? Depth);   // null = leaf chapters; ChapterMode.Leaf
public static class BookChapters                        // the import preview's chapter rule (C2)
{
    public static IReadOnlyList<ParsedChapter> Flatten(IReadOnlyList<BookSection> sections, ChapterMode mode);
}
public sealed record ParsedChapter(int Order, string Title, string Text);
```

HTTP contract for C4 (landed with C3): `POST /api/dictionaries/import` is `multipart/form-data`
with `file` (≤16 MB), `requestPublication` and `chapterMode` (int; absent = leaf); the name
comes from the book's own title, else the file name. Response: `ImportResult` plus
`translationQueued: bool`. 429 carries `Retry-After`. A refusal is
`DictionaryError{message, error?}` with `error` ∈ `invalid_book` | `encrypted_book` |
`not_english` for the cases the SPA words itself.

Where they live: `LanguageLab.Application/Translation` (`ILlmClient`, `LlmRequest`,
`LlmExceptions.cs`, `IWordBatchTranslator`, `ITranslationQueue`),
`LanguageLab.Application/Books` (`IBookParser`, `ParsedBook`, `BookChapters`, `BookFormatException`),
`LanguageLab.Domain/Lexicon` (`IEnglishLexicon`). Shared test fakes are in
`LanguageLab.Tests/Fakes/` — use them rather than writing private ones. A1 added
`StubHttpHandler` (a scripted `HttpMessageHandler`) and `ListLogger<T>` (captures log lines)
there for testing HTTP clients; A2/A3 reuse them. A1's implementations live in
`LanguageLab.Application/Translation/Llm/`; `AddLlmClient` registers the scoped `ILlmClient`.

All Wave 1 streams (A1, B1, C1, C2) have landed, so none of the W0 shapes are pending anymore.

Three points the final W0 review surfaced, settled here so A1/A2/B1 don't each answer them
differently (also written into the affected interfaces' XML doc-comments):

- **`ILlmClient.CompleteJsonAsync`'s `JsonElement`** must not depend on a `JsonDocument` the
  implementation disposes — return `RootElement.Clone()` if parsing into a document that goes
  out of scope, otherwise the caller reads a disposed document.
- **No book, sentence or word text in an exception message.** The "don't log user content" rule
  (`ILlmClient`, `ISentenceTranslator`) covers exception messages too — including a provider's
  error body, which can echo the input back — and `BookFormatException` must not embed raw file
  content either.
- **`IWordBatchTranslator.TranslateAsync` makes one provider call and does not split
  internally.** The caller keeps one call to a provider-appropriate size (B1's worker uses
  batches of about 200). A call that throws has translated none of its lemmas — there is no
  partial answer on failure, so the caller retries the whole batch it sent.

## Workstreams

### Wave 0

- [x] **W0. Contracts** — the interfaces and records above, `LlmRequest`, the exception types,
  and fakes in `LanguageLab.Tests`; no behaviour. One small commit on `dev` before any worktree
  branches off.

### A. LLM translator

- [x] **A1. LLM clients** (spec: `2026-09-28-llm-a1-clients-design.md`; wt: llm-a1) — `GeminiLlmClient` (structured output via `responseSchema`),
  `OpenAiCompatibleLlmClient` (DeepSeek, `response_format: json_object`), config + DI selection
  per Q7, `StubHandler` tests like `DeepLTranslatorTests`. *Needs:* W0.
- [ ] **A2. Word translation** — `LlmWordBatchTranslator` (batches of ~200, output accepted only
  for the lemmas requested), the single-word `ITranslator` lookup routed through it,
  `TranslationService.LookupAsync` unchanged on the cache side, 100-character word cap. Remove
  `MyMemoryTranslator`, `MyMemoryWordBudget`. *Needs:* A1.
- [ ] **A3. Sentence translation + limits** — `LlmSentenceTranslator` with the hardened prompt,
  ~500-character cap; remove `DeepLTranslator`, `MyMemorySentenceTranslator`,
  `FallbackSentenceTranslator`, `DailyCharacterBudget`, `MyMemoryDailyLimits`, the sentence
  budget and `SentenceQuota`; "1 uncached translation per 10 s" per user in `UserRateLimits` for
  `GET /api/translate` and the reader word lookup (miss only) and `POST /api/translate/sentence`
  (every request). *Needs:* A2 (same files).

### B. Background dictionary translation

- [x] **B1. Queue + worker** (spec: `2026-09-28-llm-b1-translation-queue-design.md`) — `TranslationJob` table (`DictionaryId`, `Language`, status,
  progress, attempts, unique on the pair) + migration; `ITranslationQueue`; a `BackgroundService`
  that picks a job, selects the dictionary's shared words with no `WordTranslation` in that
  language, translates them through `IWordBatchTranslator` in batches, writes `Machine` rows,
  backs off on quota, idles while unconfigured. *Needs:* W0 (tests use a fake
  `IWordBatchTranslator`).
- [ ] **B2. Triggers + status** — enqueue on import (importer's language) and on demand per Q6;
  translate a word on "don't know" (`WordSortingService.MarkAsync`) when it has no translation
  yet; expose the job's "N of M" on the dictionary endpoint and show it on the dictionary screen.
  *Needs:* B1.

### C. Server-side import

- [x] **C1. English lexicon** (spec: `2026-09-28-llm-c1-english-lexicon-design.md`; wt: llm-c1) — build script under `scripts/` (SCOWL/AGID, size 60 per Q3), the
  data file as an embedded resource, `EnglishLexicon : IEnglishLexicon`, and the same table as a
  static SPA asset with a small TS `lemmaOf` loader + tests. The data is generated: C1 documents
  `scripts/build_lexicon.py` in `CLAUDE.md` (Project layout, plus an invariant: never edit
  `english-lexicon.txt` by hand — change `scripts/lexicon-overrides.txt` and rerun), README and
  `docs/architecture.md`. *Needs:* W0.
- [x] **C2. Book parsers on .NET** (spec: `2026-09-28-llm-c2-book-parsers-design.md`; wt: llm-c2) — port `web/src/books/` (`format.ts` sniffing, `decode.ts`
  encodings incl. windows-1251, fb2, `epub.ts` spine + TOC `#id` cuts,
  DRM → `Encrypted`) to `IBookParser`. Port the frontend's test books as fixtures. *Needs:* W0.
- [x] **C3. Import endpoint** — multipart upload, server SHA-256 as `FileHash` (drop the
  `ReaderBook`-must-exist check, since the hash is now proven), tokenize → `IEnglishLexicon` →
  counts per chapter → existing `BookImportService` core; 1 import/day, admins exempt (Q5);
  enqueue B. *Needs:* C1, C2, B1.
- [ ] **C4. Frontend** — `ImportScreen` uploads the file with progress; delete
  `parseBook.worker.ts`, `web/src/fb2/aggregate.ts`/`lemmatize.ts`, `useAutoImport.ts` and the
  `compromise` / `wink-lemmatizer` deps; switch the reader's highlights and word panel to the C1
  `lemmaOf` loader; the reader's **Build dictionary** button (Q4); re-enable import inside the
  Telegram Mini App. *Needs:* the HTTP contract above (can start against a mocked API), C1's TS
  loader.

### D. Wrap-up

- [ ] **D1. Docs + TODO** — update `docs/vocabulary-and-training.md`, `docs/reader.md`,
  `docs/architecture.md`, README config section (new keys, removed MyMemory/DeepL keys, `.env`);
  mark closed TODO items; add any new deferrals.

## Parallel plan (worktrees)

```
Wave 0  (dev, one session)   W0 contracts + fakes
          │
Wave 1  ┌─ A1 LLM clients ─┐   ┌─ C1 lexicon ─┐   ┌─ C2 parsers ─┐   ┌─ B1 queue (fake translator) ─┐
(4 wt)  │                  │   │              │   │              │   │                              │
Wave 2  ├─ A2 words → A3   │   ├─ C4 frontend │   └──────┬───────┘   ├─ B2 triggers + status        │
(4 wt)  │   sentences      │   │ (mocked API) │          │           │                              │
        │                  │   └──────────────┴── C3 import ◄────────┘ (needs C1, C2, B1)           │
Wave 3  └────────────── merge to dev, D1 docs, end-to-end check with a real Gemini key ────────────┘
```

- Wave 1 is four fully independent worktrees: **A1**, **C1**, **C2**, **B1**. They touch
  disjoint folders; only B1 adds a migration.
- Wave 2 is four worktrees: **A2→A3** (one worktree, sequential — same files), **B2**, **C3**,
  **C4**. C4 can start as soon as C1 lands, even before Wave 1 finishes.
- Migrations: only B1 creates one. If another stream needs one, rebase on B1 first — two
  migrations generated in parallel on the same snapshot conflict in
  `ApplicationDbContextModelSnapshot`.
- `Program.cs` DI registration is the usual merge hotspot: each stream adds its registrations in
  its own `Add…` extension method and touches `Program.cs` with one line.

## Picking up a workstream in a new session

1. Read this file; pick an unchecked item whose *Needs* are all `[x]`.
2. Mark it `[~]` with the branch name, e.g. `[~] (wt: llm-a1)`. This file is the one spec under
   `docs/superpowers/` that git tracks (see `.gitignore`), so each worktree carries its own copy.
   Status marks (`[ ]`/`[~]`/`[x]`) are made in the **main checkout's** copy, which every local
   session can read at its absolute path; a stream's branch changes only the Decisions, Contracts
   and its own progress-log line, so merges stay free of conflicts.
3. Create a worktree off `dev`, brainstorm/write the stream's own spec in
   `docs/superpowers/specs/`, then its plan in `docs/superpowers/plans/`.
4. Implement; commits only as the repo's `CLAUDE.md` allows (the user approves each commit and
   every push; step-by-step commits are squashed into one before hand-off).
5. When merged into `dev`, mark it `[x]` and add a line to the progress log. A contract or
   decision change goes into the sections above, not only into the stream's spec.

## README TODO items this closes

- [ ] Auto-translate on book import and on "don't know" — B1 + B2
- [ ] Book import inside the Telegram Mini App — C3 + C4
- [ ] Flaky `ImportScreen.test.tsx` worker-parse previews — C4 (the worker goes away)
- [ ] One-off audit of shared `WordPair` rows that predate the import word rule — partially: C1
  gives the criterion; the cleanup itself stays a TODO
- Not closed, more relevant: admin review of machine translations; FileHash collision in the
  moderation queue (C3 makes the hash genuine but does not surface the collision)

## Progress log

- 2026-09-27 — Roadmap written; Q1–Q7 settled (see Decisions).
- 2026-09-27 — W0 landed on dev. IBookParser.Parse takes byte[] (ZipArchive/XmlReader need a Stream).
- 2026-09-28 — A1 landed on dev: `GeminiLlmClient`, `OpenAiCompatibleLlmClient`, `LlmClientSelector`
  via `AddLlmClient` (`LanguageLab.Application/Translation/Llm/`); `LlmRequest`'s doc-comment
  amended (the client guarantees a JSON object, the consumer checks its shape). Live Gemini check:
  confirmed on 2026-09-28 (x-goog-api-key header, thinkingLevel "minimal", candidates[0].content.parts[].text).
- 2026-09-28 — B1 ready on llm-b1: ITranslationQueue.EnqueueAsync takes a long id; TranslationJobs table (migration AddTranslationJobs), TranslationQueue, TranslationJobProcessor, hosted TranslationWorker, NullWordBatchTranslator until A2. An unavailable batch pauses the worker 1 min (spec amended). The live enqueue-and-race check waits for A2.
- 2026-09-28 — C1 landed on dev: `english-lexicon.txt` (92823 forms, 1.37 MB) from SCOWL
  2020.12.07 size 60 + AGID @ b22230cc5250, embedded (`EnglishLexicon`, `AddEnglishLexicon`) and
  served at `/lexicon/english-lexicon.txt` (`web/src/lexicon/lexicon.ts`). `IEnglishLexicon`
  gained `LemmasOf`; 21 overrides (`lay` plus 20 common-word primaries the final review found
  buried behind a rarer AGID-generated comparative/superlative, e.g. `number`, `interest`).
- 2026-09-28 — C2 merged into dev: `BookParser : IBookParser` (fb2 on System.Xml, epub on AngleSharp +
  AngleSharp.Xml). `ParsedBook` is now a section tree; `BookChapters.Flatten(sections, ChapterMode)`
  yields the chapters, so the C3/C4 upload carries the picker's `chapterMode` (null = leaf). Every
  refusal is `BookFormatException(Invalid|Encrypted)`, including the parser's limits: zip 5,000
  entries / 32 MB per entry / 64 MB total, nesting 256, 30 s per book. No file-name fallback: `Title`
  is "" when the book names none (C3's job).
- 2026-09-29 — C3 merged into dev: `POST /api/dictionaries/import` takes the file itself
  (multipart + `chapterMode`); server SHA-256 is the `FileHash` (ReaderBook vouching dropped);
  `ImportTokenizer` keeps only lexicon lemmas and refuses < 50 % known occurrences as
  `not_english` (`DictionaryError.Error` codes for C4); `ImportResult` + `translationQueued`;
  enqueue for the importer's language on import; 1 import/day (admins exempt), 429s carry
  `Retry-After`; `ImportWordText.MinLength` 3 → 2. The SPA still sends the old JSON until C4 —
  import in the UI is down on dev until then (accepted).
