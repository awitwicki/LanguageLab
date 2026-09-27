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

In `BookImportService` and `LanguageLab.Domain`:

- A word must be lowercase ASCII letters, 3–64 characters (`ImportWordText`), or it is dropped from
  the import.
- An import where more than 20% of its distinct words are invalid is refused outright.
- Limits: 50,000 distinct words and 2,000 chapters per import
  (`BookImportService.MaxWords`/`MaxChapters`); names and chapter titles truncated at 300
  characters (`TitleText.MaxLength`); a 16 MB request-body cap on `/api/dictionaries/import`
  itself; bulk personal-word import capped at 500 entries
  (`PersonalDictionaryService.MaxBulkEntries`).

`Dictionary.FileHash` — set on import, a SHA-256 of the book file, see [reader.md](reader.md) — is
kept only when the importer's own `ReaderBook` library already holds that hash; otherwise it is
dropped even if the client sent one. This is not proof the hash is genuine, since a client can
register any hash first, but it raises the bar past a casual collision or a drive-by import with no
`ReaderBook` at all. The real backstop against a stranger's junk import reaching other readers is
publication review, not this check: an unreviewed import is `Private`, invisible to everyone but
its owner and admins regardless of what hash it claims.

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
| `POST /api/dictionaries/import` | 20 requests |
| `GET /api/translate` | 500 lookups |
| bulk personal-word import | 20 requests |

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
