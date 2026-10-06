# Grammar roadmap

A future top-level mode: grammar topics in a fixed order, each with a short explanation and
interactive exercises. This file is the plan, not a spec — each phase below gets its own spec and
implementation plan when it is picked up. README → TODO points here.

## Shape

A domain of its own, like the irregular verbs (see [trainers.md](trainers.md)): topics, explanations
and exercises live in code (`LanguageLab.Domain/Grammar/` — `GrammarSyllabus` lists every topic,
`GrammarCatalog` holds the written ones' content, the way `IrregularVerbCatalog` does), with
no `WordPair`, no dictionary and no generation at run time. Only the learner's progress is stored.

## Content and copyright

All content is written for this project. Reference grammars (Eastwood's *Oxford Guide to English
Grammar*, Murphy, Swan) may be consulted to check coverage, but nothing is copied from them:

- **Free to use:** the rules of the language themselves and the conventional order of topics
  (tenses → modals → passive → conditionals …), which every grammar shares.
- **Never copied:** a book's explanations, example sentences, exercises or answer keys.
- **Not mirrored:** a book's own section numbering and breakdown — the syllabus below is organised by
  CEFR level instead.

Example sentences may be drafted with an LLM; every one is read and corrected by a person before it
ships.

## Syllabus (A1–B2)

The syllabus lives in code: `GrammarSyllabus` (`LanguageLab.Domain/Grammar/`) — 86 topics in 15
sections, each with its key, section, CEFR level and title, in learning order. Sections follow
grammar areas, so one can run from A1 to B2. The Grammar screen lists them all; a topic without
content in `GrammarCatalog` shows as "Coming later". The levels were signed off on 2026-10-06
(spec: `docs/superpowers/specs/2026-10-06-grammar-level-goal-design.md`).

## Phases

- **G0 — Open decisions** (settled in G1's spec):
  - the language of the explanations: English like the rest of the UI, or the learner's language
    like `WordTranslation.Text`. If it is the learner's language, the mode is Ukrainian-only at
    first, as the irregular verbs are (`visibleModes`);
  - the progress model: per-topic mastery like the verbs drill, or Leitner review across topics;
  - which sections stay free if the mode is ever paid.
- **G1 — Engine MVP** — done: the `grammar` mode, a topic list, a topic screen with its explanation,
  pick-the-form exercises judged in the browser, and two topics from section 1 (`be`,
  `there is / are`) with five exercises each (six topics now). Nothing is stored (see
  [trainers.md](trainers.md#grammar)). Catalog tests keep every item to one right answer and
  distinct options. Left for G3: a type-the-form exercise and per-topic progress on the server.
- **G2 — Syllabus sign-off** — done: the syllabus is `GrammarSyllabus`, A1–B2.
- **G3 — Content, section by section:** each batch is one section — explanations plus 25–40 items a
  topic, drafted, reviewed by a person, and covered by the catalog tests. A third exercise type
  (find the mistake, or put the words in order) when a section needs it.
- **G4 — Later:** mixed revision across finished topics; a placement test to start at the right level; links from
  the verbs mode to the past simple and present perfect topics.
