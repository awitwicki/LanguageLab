# Vocabulary, import and training

How words get into the database, who may see them, how they are translated, and how training
draws on them.

## Dictionaries and publication

Dictionaries have an owner and a `PublicationStatus` — `Private | Pending | Published | Rejected`,
defaulting to `Private` — in place of the old `IsPublic` flag.

`POST /api/dictionaries/import` takes no role. Its `requestPublication` flag only asks;
`BookImportService.StatusFor(role, requestPublication)` decides the outcome:

| Requested | Importer | Result |
|---|---|---|
| no | anyone | `Private` |
| yes | `CanPublishDirectly(role)` — see [auth.md](auth.md) | `Published` |
| yes | anyone else | `Pending` |

The owner offers or withdraws the request afterwards with
`POST|DELETE /api/dictionaries/{id}/publication`, and an admin decides from the moderation queue
(`GET /api/admin/dictionaries`, `POST /api/admin/dictionaries/{id}/approve|reject`).

Non-admins see `Published` dictionaries plus their own; admins see every non-personal one.

Deletion (`DELETE /api/dictionaries/{id}`) stays admin-only, and also removes any shared `WordPair`
rows the deletion orphans — rows left in no dictionary and carrying no shelf, progress or training
row for anyone (`DictionaryDeletionService`). `DELETE /api/admin/users/{id}/dictionaries`
bulk-deletes a user's dictionaries the same way, for use alongside a ban.

## Import validation

In `BookFileImportService`, `ImportTokenizer`, `BookImportService` and `LanguageLab.Domain`:

- `POST /api/dictionaries/import` is `multipart/form-data`: `file` (the fb2/epub/zip bytes),
  `requestPublication` and `chapterMode` (chapter depth; absent = leaf, the same rule as the
  import screen's preview). The server hashes the bytes (SHA-256 → `Dictionary.FileHash` — the
  hash is now the server's own, so nothing vouches for it), parses them with `IBookParser`, cuts
  chapters with `BookChapters.Flatten`, and tokenizes each chapter: clean a-z tokens go through
  the English lexicon, and only known lemmas are kept — names, gibberish and non-English words
  are dropped.
- A stored word is lowercase ASCII letters, 2–64 characters (`ImportWordText`); two-letter words
  are importable only because the lexicon vouches for them (`go`, `ox`).
- An upload whose text is less than half lexicon-known occurrences (stop words count as known)
  is refused as `not_english`; an unreadable or DRM-protected file is `invalid_book` /
  `encrypted_book` (`DictionaryError.Error` — the SPA words these for the user).
- On success the response is `ImportResult` plus `translationQueued`: the dictionary is queued
  for background translation into the importer's language (a learner with no language picked
  imports fine, just unqueued).
- Limits: 50,000 distinct words and 2,000 chapters per import
  (`BookImportService.MaxWords`/`MaxChapters`); names and chapter titles truncated at 300
  characters (`TitleText.MaxLength`); a 16 MB request cap on the upload; bulk personal-word
  import capped at 500 entries (`PersonalDictionaryService.MaxBulkEntries`).

## The personal dictionary

One private `Dictionary` per user (`IsPersonal`), created on first use by `GET /api/dictionaries`.
Its words are `WordPair` rows with `OwnerId` set — unique on `(Word, OwnerId)` with
`NULLS NOT DISTINCT` — shelved "don't know" on add.

`PersonalDictionaryService.UpdateTranslationAsync` corrects the meaning of a word already added
and marks it `Manual`; the word text itself is not editable, since it is what the uniqueness is
built on. The shelf and the `WordProgress` row survive the edit, and a training session already
running shows the new text on its next question — `TrainingQuestion` stores word ids, never the
words themselves.

**Shared vocabulary is `OwnerId IS NULL`, and any lookup by word text must filter on it.**

## Translation

Every machine translation — a word, a batch of a dictionary's words, a reader sentence — comes from
one language model behind `ILlmClient` (`LanguageLab.Application/Translation/Llm/`): Gemini by
default, or any OpenAI-compatible endpoint (DeepSeek by default) with
`Translation:Provider=OpenAiCompatible`. The keys are listed in the README. Without the selected
provider's API key the app still starts — in Production too — logs a warning, and translation is
off: a lookup answers "no translation", the background queue idles, and the reader hides sentence
translation. There is no non-LLM fallback.

On top of `ILlmClient`:

- `LlmWordBatchTranslator` (`IWordBatchTranslator`) — up to ~200 lemmas in one call, keeping an
  answer only for a lemma it was asked about. A translation spelled the same as the English word
  is kept: several languages share some spellings with English, so it is not an echo.
- `LlmTranslator` (`ITranslator`) — one word as a batch of one; never throws, and refuses a word
  over 100 characters without asking.
- `LlmSentenceTranslator` (`ISentenceTranslator`) — see [Sentence translation](reader.md#sentence-translation).

Every prompt is a fixed system instruction; the user's text travels only as data, the answer is
read only from the fields of a constrained JSON schema, and no book, sentence or word text is ever
logged or put in an exception message. Each HTTP call times out after 30 seconds (`LlmHttp.Timeout`).

A word's meaning lives in `WordTranslation` (table `WordTranslations`), not on `WordPair` itself:
`WordPairId` + `Language` (a `LearnerLanguages` code) is unique, `Text` is required and non-empty,
and `Origin` is `Manual` or `Machine`. A row exists only when there is a translation — there is no
more "untranslated" sentinel value, only the absence of a row for that language. A shared `WordPair`
can carry a different translation per language at once; a personal word (`OwnerId` set) keeps one
translation per language too, so switching languages does not lose what was typed for another one.
`Text` is at most `WordTranslation.MaxTextLength` (200) characters — enforced where text comes in
(typed in `PersonalDictionaryService`, or a model answer in `TranslationService`), not in the schema,
so a migration never fails on an old row.

Single-word lookups (`TranslationService.LookupAsync`, used by `GET /api/translate`, the reader's
word panel and the sorting "don't know" mark) are cached into the shared vocabulary as a `Machine`
`WordTranslation` in the language asked for, so the same word costs the model at most once per
language. A lookup adds a *new* shared word only under the rule book import follows — an English
lemma, one `ImportWordText` accepts and `IEnglishLexicon` knows as its own lemma; a word already in
the shared vocabulary is translated in place whatever the lexicon says. A phrase, an inflected form
or a made-up string (where a prompt injection would live) is translated and answered but never
stored, so the reader's "Add to training" sends that translation along with the word. A model answer longer than `MaxTextLength` counts as no answer. A `Manual` translation already there for that language is never overwritten, and a hit
in one language says nothing about any other. A hit is free; a miss spends the user's
[uncached-translation slot](#uncached-translation-limit).

**A word without a `WordTranslation` in the learner's current language is neither learnable, nor
due, nor a distractor** — `WordSelectionService`'s learnable query, due-words query and distractor
pool, and `QuestionQueueBuilder`'s option list, all filter on it. A personal word or a due Leitner
row that has no translation in the language just switched to waits untranslated until the learner
adds one or switches back.

Training only pulls new words from a specific dictionary, so a cached word becomes trainable only
once it belongs to one — which is why the reader's "Add to training" shelves a word in the
dictionary of the book being read when it can, and falls back to "My words" otherwise
(`ReaderWordService.LearnTargetAsync`, in [reader.md](reader.md)).

**Migration note.** The `MultilingualTranslations` migration moved every pre-existing
`WordPair.Translation` into `WordTranslations` as a `uk` row (shared and personal words alike, origin
carried over from the old `TranslationOrigin` column), then dropped the two old columns. Every
account that predates the language picker was set to `uk` at the same time.

### Background translation queue

`ITranslationQueue.EnqueueAsync(dictionaryId, language)` (`TranslationQueue`, in
`LanguageLab.Application/Translation/Queue/`) asks for a dictionary's shared words that have no
`WordTranslation` in that language to be translated in the background. There is one
`TranslationJob` row per (dictionary, language), unique in the database: enqueueing is a no-op
while the job is pending and starts a fresh pass once it has completed or failed. Personal
dictionaries are never queued.

The hosted `TranslationWorker` takes one batch of one job at a time (`TranslationJobProcessor`),
round-robin — the pending job processed longest ago goes next — so a large book never holds up a
small one. A batch is up to 200 of the job's words, most frequent first, sent through
`IWordBatchTranslator`; each answer becomes a `Machine` `WordTranslation`, unless a translation in
that language appeared meanwhile. A pass walks the words once: a word the model gave nothing for
is passed over until the next enqueue. A quota refusal pauses the whole worker (the provider's
`Retry-After`, otherwise 1 minute doubling up to an hour); an unavailable batch waits a minute,
and five in a row mark the job `Failed`. Without a configured language model
(`IWordBatchTranslator.IsConfigured` false) the worker idles, and the dictionary screen shows no
progress for a job that cannot advance.

Three things enqueue a dictionary: import, for the importer's language
(`BookFileImportService.ImportAsync`); opening it — `GET /api/dictionaries/{id}`, and the reader
linking a book to one via `PUT /api/reader/books/{hash}` — for the caller's current language, freely
on every open (`ReaderEndpoints.MaybeEnqueueTranslationAsync`); and marking a shared word "don't
know" (`POST /api/sorting/mark`), which translates that one word directly instead of enqueueing the
whole dictionary (`SortingEndpoints.TranslateIfUnknownAsync`, reusing `TranslationService.LookupAsync`).
`GET /api/dictionaries/{id}` also reads the matching `TranslationJob` back
(`TranslationJobProgressReader`) as `DictionaryDetail.Translation` — `{ done, total }` while a job is
`Pending` with work left, else `null` — which the dictionary screen shows as "Translating… N of M",
polling every 4 seconds until it clears.

### Uncached translation limit

One translation that reaches the model per user every 10 seconds, and at most
`UncachedTranslationLimiter.DailyLimit` (300) per UTC day, admins included
(`UncachedTranslationLimiter`: one entry per user, claimed by compare-and-swap, so two concurrent
misses cannot both get through). Sentences and word-lookup misses share the slot and the budget; a
refusal past the daily cap names the wait until UTC midnight. It is
checked inside `TranslationService.LookupAsync` at the cache-miss point, and by
`POST /api/translate/sentence` before every model call — there is no server-side sentence cache,
so every sentence spends it. How a refusal looks:

- `GET /api/translate` and `POST /api/translate/sentence` answer 429 with `Retry-After`.
- The reader's `GET /api/reader/words/{lemma}` stays 200, with `source: "rateLimited"` and
  `retryAfterSeconds`, so the word panel keeps its buttons and says when to try again.
- The sorting "don't know" mark just leaves the word untranslated; the background queue picks it
  up on the dictionary's next open.

The background queue does not go through this limit — it is the server's own work, not a user's.

### Per-user rate limits

Also in memory — a restart forgives everybody — and per day:

| What | Limit | Where |
|---|---|---|
| Book import | 1 *successful* import (admins exempt) | `ImportQuota` |
| `POST /api/dictionaries/import` | 20 attempts | `UserRateLimits.ImportAttempts` |
| `GET /api/translate` | 500 lookups | `UserRateLimits.Translate` |
| bulk personal-word import | 20 requests | `UserRateLimits.BulkWords` |
| single-word writes: add / edit a personal word, the reader's learn / known / ignore | 2,000 requests | `UserRateLimits.WordWrites` |
| model calls, every path | 300 (and one per 10 s) | `UncachedTranslationLimiter` |

`ImportQuota` counts only an import that succeeded, so a wrong file, DRM or a non-English book is
immediately retryable: `TryReserve` checks and reserves the slot in one atomic step and a failed
import releases it — but only up to the save: once the dictionary is saved the slot stays spent,
even if the client hangs up before the translation enqueue (which ignores the request's token). The looser 20-attempt policy, checked by the middleware before the handler
runs, stops a script from hammering the endpoint with garbage. `GET /api/reader/capabilities` reports
the wait as `importRetryAfterSeconds` (a read-only peek), so the reader's **Build dictionary**
button can show it without a click.

A refused request answers 429 with a `Retry-After` header naming the wait in seconds.

## Training

Training requires a `WordTranslation` in the learner's language, both for batch words and for
distractors; see [Translation](#translation) above. A session is built once and renders in
`Training.Language` from then on, so a language switch mid-session never blanks a button.
Translations for the "don't know" shelf were backfilled once on 2026-09-07
(`result/translations.txt`, local, `uk` only); since then the
[background queue](#background-translation-queue) and the "don't know" mark fill in what is
missing.

A batch is the scope's most frequent learnable words, by chapter or book frequency, and is
deterministic. The web app shows a preview and passes `wordPairIds` explicitly.

Per-scope Leitner progress lives in `LearningProgressService`; the endpoints that expose it are
listed in [architecture.md](architecture.md#training).
