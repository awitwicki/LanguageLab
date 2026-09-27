# The irregular-verbs and pronunciation trainers

Both are domains of their own, separate from dictionaries and from each other: their content lives
in code, and neither touches `WordPair` or needs seeding.

## Irregular verbs

The 68 verbs and their examples live in code
(`LanguageLab.Domain/IrregularVerbs/IrregularVerbCatalog`), grouped into the four stages the
learner sees — one per verb type, all open from the start, with no families and nothing locked.

**Ukrainian learners only** — the catalog's translations are Ukrainian in code, so
`/api/irregular-verbs/*` (`IrregularVerbEndpoints.IsAvailableFor`) answers `404` for every other
`LearnerLanguages` code, and `web/src/layout/mode.ts`'s `visibleModes` hides "Irregular verbs" from
the top bar the same way. A learner's `VerbKnowledge`/`VerbAnswer` rows are untouched by the
language they are on, so switching back to Ukrainian brings the progress back exactly as it was.

### The session

The browser is handed a whole session rather than one card at a time: the words in play, their
standing, their three example sentences and a per-verb order of the three forms. Both rounds then
run in the client (`web/src/verbs/session.ts`), so no click waits on the network, and answers leave
through a background outbox (`web/src/verbs/outbox.ts`).

Ordinary training opens on a start screen — the words coming up, a stepper for how many of them
(1–5, default 5) and one for how many introduction rounds (1–5, default 3), remembered per device in
`localStorage`. It opens every time, because an introduction already done and a batch not yet
learned is exactly when it is wanted again.

The **introduction round** walks the chosen words round-robin, N·R cards, each pass taking the next
form of the word's own order. It has one Next button, which moves on whether or not the answer is
uncovered, and it is not recorded at all — there is no verdict in it to grade.

The **drill round** then takes those same words to four "I know" in a row each. "I know" goes
straight to the next card; "I don't know" uncovers the three forms and the example sentence and waits
for Next, which is the moment the word is learned. Tapping the blur uncovers it and judges nothing,
leaving the verdict still to give while the clock runs.

That verdict plus how long the click took is the whole signal. `VerbScoring` gives quality 1.0 for a
click within 1.5 s, down to 0.45 at 6 s, and 0 for a miss, folded into `Mastery` as a moving average
with α = 0.4.

`Streak` — consecutive "I know" answers, four of them to pass a verb — drives nothing but batch
progression. Inside a session the browser counts it locally, seeded from the server's row; the
server's own count is whatever `VerbScoring.Replay` folds out of the answer log.

### Batches and free training

Ordinary training is the first five not-passed verbs of the stage (`BatchWindow.Of`), handed over
least-known first (`BatchWindow.Ordered`) so a session shortened to three words keeps the three the
learner knows least. A new window opens only once the previous one's words have passed, and the
client flushes its outbox before asking for it — the window is computed from what the server has
recorded.

Free training draws at random with 65% of cards from the weak words (`FreePicker`), over one stage, a
stage and every earlier one, or all 68. It is handed a 20-card queue (`FreePicker.Draw`) and the
client fetches the next chunk with five cards left, so the queue never runs dry. A chunk is drawn on
the standing at request time and does not see the answers given inside itself.

### Storage

A verb's standing is one `VerbKnowledge` row, and every judged card is appended to `VerbAnswer`. The
row is a cache of that log, replayed from it on every answer (`VerbScoring.Replay`) rather than
incremented, so two devices answering at once cannot leave the counters behind the log.

The server keeps no session and no queue. The device does: as a round is played, the browser saves
it to `localStorage` (`web/src/verbs/savedSession.ts`) — the card the learner lands on next, the
drill round's streaks, a free run's queue, and the answers the outbox has not sent yet, re-saved on
every change to that queue so a resume never posts one twice. After a reload the Verbs screen offers
the round back ("Resume session" or "Discard"). The saved round belongs to one user and lapses after
12 hours; Back, a passed batch, or Discard forget it, and a round's unsent answers still go out
whichever way it ends, including when a fresh round is started over it.

### API and presentation

`/api/irregular-verbs` — `GET /progress`, `GET /session?mode=batch|free&group=&scope=stage|cumulative|all`,
`POST /answers`. The query string is parsed by hand (`ParseSession`), because minimal API binds a
query enum case-sensitively. `GET /session` answers `204` when ordinary training has passed every
verb of its stage.

`POST /answers` takes an array of up to 100 answers and applies them in one transaction: the browser
retries a failed request, and with no answer identity to deduplicate against, a half-applied chunk
would count its first answers twice. One invalid entry — an unknown verb included — refuses the whole
body.

The table's colour comes from `Mastery` as a band (`mastery.ts`, `--mastery-*` tokens), with the
bar's length carrying the exact level.

## Pronunciation

A curated catalog of English words
(`LanguageLab.Domain/Pronunciation/PronunciationCatalog`), generated by
`generate_pronunciation_catalog.py`: CMU-dictionary-driven set-cover word selection per sound
family, with real US/UK recordings from Wiktionary committed under
`web/public/pronunciation-audio/`.

The words are grouped into sound families that are all open from the start;
`PronunciationLearningPath` only marks one done at 80% mastered, and nothing is locked.

A user's standing on a word (`PronunciationProgress`: `New → Learning → Mastered`) drives which word
is served next. An attempt is graded server-side by comparing the browser's `SpeechRecognition`
transcript against the target word — `PronunciationAnswerChecker`, a word-recognition proxy for
pronunciation quality rather than phoneme-level scoring — and every attempt is logged append-only
(`PronunciationAttempt`).

`/api/pronunciation` — `GET /progress`, `GET /families/{key}`, `GET /families/{key}/next`,
`POST /words/{word}/attempts`, and `DELETE /words/{word}/progress`, which is the word card's Reset
progress: it drops the standing and keeps the attempt log.

Practice is gated off in browsers without `SpeechRecognition` (Firefox, Safari) — there is no
degraded drill. The alphabet below is the one part of the mode that still shows there.

### The alphabet

The reference half of the mode: the whole IPA to look a symbol up in, rather than to drill.
`IpaCatalog` (`LanguageLab.Domain/Pronunciation`) holds 124 symbols in six sections — pulmonic and
non-pulmonic consonants, the other symbols, vowels, the English diphthongs, and the marks that
change how the letter beside them is read. Each one carries its phonetic name, a plain-language
hint, an example word with its language and transcription, and up to two recordings: the sound on
its own and the example word said in full.

`generate_ipa_catalog.py` writes `IpaCatalog.Generated.cs`. The symbol table in that script is
hand-curated and is the source of truth; only the audio comes from the network — a sound's own clip
from Commons (by phonetic name, falling back to the file its Wikipedia article carries, which is
what handles Commons spelling the glottal plosive "Glottal stop"), and an example word's clip from
Wiktionary, reusing a clip the trainer already committed when the word is one of its own. Only a
file that downloaded is named in the catalog, so a symbol whose recording cannot be found is silent
rather than broken; 108 of the 124 have a sound of their own, and the rest lean on their example
word. A handful of symbols Commons names unrecognisably carry a `sound_file` override in the table.

`IpaCatalog.FamilyKeyFor` reads the trainer's families live rather than baking the link into the
generated file, so the "Practice it" link and a family's target sounds cannot drift apart. Only a
sound English uses can link: the trainer's `r` means the English approximant ɹ, not the trill that
owns the letter r. `Aliases` carry the other spellings a learner may arrive with — `iː` for `i`, the
r-coloured `ɚ` for the schwa — and search matches them too.

`GET /api/pronunciation/alphabet` serves the chart, built once at startup: it reads no user state,
so there is nothing per-request to compute.
