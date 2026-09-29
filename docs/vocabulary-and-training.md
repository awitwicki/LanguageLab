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

`Translation:MyMemoryEmail` and `Translation:DeepLApiKey` are both optional config.

A word's meaning lives in `WordTranslation` (table `WordTranslations`), not on `WordPair` itself:
`WordPairId` + `Language` (a `LearnerLanguages` code) is unique, `Text` is required and non-empty,
and `Origin` is `Manual` or `Machine`. A row exists only when there is a translation — there is no
more "untranslated" sentinel value, only the absence of a row for that language. A shared `WordPair`
can carry a different translation per language at once; a personal word (`OwnerId` set) keeps one
translation per language too, so switching languages does not lose what was typed for another one.

Provider word translations (`TranslationService.LookupAsync`, used by both `GET /api/translate` and
the reader's word panel) are cached into the shared vocabulary as a `Machine` `WordTranslation` in
the language asked for, so the same word costs the network at most once per language. A `Manual`
translation already there for that language is never overwritten, and a hit in one language says
nothing about any other.

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
and five in a row mark the job `Failed`. Without a configured translator (`NullWordBatchTranslator`
until the LLM translator lands) the worker idles.

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

### Budgets

Without `Translation:DeepLApiKey`, MyMemory translates sentences too, capped by
`MyMemorySentenceBudget`: a server-wide daily budget of 40% of MyMemory's own daily limit — 2,000
characters without `Translation:MyMemoryEmail`, 20,000 with it — spent only on a sentence actually
sent, so the reader cannot exhaust the quota the word lookups also share. The word lookups get the
remaining 60% as `MyMemoryWordBudget`, the same `DailyCharacterBudget` mechanism
(`LanguageLab.Application/Translation/DailyCharacterBudget.cs`) wrapped the other way round.

### Per-user rate limits

`UserRateLimits` — sliding windows, in-memory like `SentenceQuota` — sits in front of the three
endpoints one account could otherwise make expensive for everybody, per day:

| Endpoint | Limit |
|---|---|
| `POST /api/dictionaries/import` | 1 request (admins exempt) |
| `GET /api/translate` | 500 lookups |
| bulk personal-word import | 20 requests |

A refused request answers 429 with a `Retry-After` header naming the wait in seconds.

## Training

Training requires a `WordTranslation` in the learner's language, both for batch words and for
distractors; see [Translation](#translation) above. A session is built once and renders in
`Training.Language` from then on, so a language switch mid-session never blanks a button.
Translations for the "don't know" shelf were backfilled once on 2026-09-07
(`result/translations.txt`, local, `uk` only); auto-translation is still in the README TODO.

A batch is the scope's most frequent learnable words, by chapter or book frequency, and is
deterministic. The web app shows a preview and passes `wordPairIds` explicitly.

Per-scope Leitner progress lives in `LearningProgressService`; the endpoints that expose it are
listed in [architecture.md](architecture.md#training).
