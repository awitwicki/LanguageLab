import { cleanWord, isRejected } from '../fb2/tokenize'
import type { Lexicon } from '../lexicon/lexicon'
import type { ReaderBook } from './readerBook'

export type WordStatus = 'new' | 'learning' | 'known'

/** The learner's standing by base form; a form missing here is new. From GET /api/reader/word-statuses. */
export type KnownStatuses = ReadonlyMap<string, 'learning' | 'known'>

export const EMPTY_STATUSES: KnownStatuses = new Map()

/** status null: a function word (the, of, don't) or a word the lexicon does not know — tappable, never highlighted. */
export interface ResolvedWord {
  lemma: string
  status: WordStatus | null
}

interface Analysis {
  word: string
  /** null for a function word or an unrecognized word; otherwise every lemma the lexicon has for it, primary first. */
  candidates: readonly string[] | null
}

// A book repeats its words endlessly: analyse each spelling once per page load, once the
// lexicon is in hand. Never populated while the lexicon is still null — see analyse() below.
const analyses = new Map<string, Analysis | null>()

/** A 2-letter form only counts when the lexicon itself treats it as a lemma (C1→C3→C4: go, ox). */
function candidatesFor(word: string, lexicon: Lexicon): readonly string[] | null {
  if (word.length < 2 || isRejected(word)) {
    return null
  }

  if (word.length === 2 && lexicon.lemmaOf(word) !== word) {
    return null
  }

  const lemmas = lexicon.lemmasOf(word)

  return lemmas.length > 0 ? lemmas : null
}

function analyse(token: string, lexicon: Lexicon | null): Analysis | null {
  // Cache reads/writes are skipped entirely while the lexicon has not loaded yet: an early
  // answer must never get stuck once a real lexicon is available (Review Focus).
  const cached = lexicon !== null ? analyses.get(token) : undefined

  if (cached !== undefined) {
    return cached
  }

  const word = cleanWord(token)

  // Only letters, apostrophes and hyphens make a word the API accepts (WordText on the server).
  const shapeOk = /^[a-z'-]+$/.test(word) && /[a-z]/.test(word)

  const analysis: Analysis | null = !shapeOk
    ? null
    : { word, candidates: lexicon === null ? null : candidatesFor(word, lexicon) }

  if (lexicon !== null) {
    analyses.set(token, analysis)
  }

  return analysis
}

/**
 * A token of the text → its base form and the learner's standing on it, resolved against the
 * shared English lexicon (web/src/lexicon/lexicon.ts) rather than a guess: `lexicon` null means
 * it has not loaded yet (or failed), and every word is treated as unrecognized until it has —
 * tappable, never highlighted, exactly like a function word.
 */
export function resolveWord(token: string, statuses: KnownStatuses, lexicon: Lexicon | null): ResolvedWord | null {
  const analysis = analyse(token, lexicon)

  if (analysis === null) {
    return null
  }

  if (analysis.candidates === null) {
    return { lemma: analysis.word, status: null }
  }

  for (const candidate of analysis.candidates) {
    const status = statuses.get(candidate)

    if (status) {
      return { lemma: candidate, status }
    }
  }

  // lemmasOf is primary-first, so the first candidate is the right lemma for a new word — not
  // the first one that merely differs from the raw token (that was a wink-lemmatizer-era
  // workaround; the lexicon is authoritative and needs no second-guessing).
  return { lemma: analysis.candidates[0], status: 'new' }
}

export function toStatusMap(statuses: { learning: string[]; known: string[] }): Map<string, 'learning' | 'known'> {
  const map = new Map<string, 'learning' | 'known'>()

  for (const word of statuses.known) {
    map.set(word, 'known')
  }

  for (const word of statuses.learning) {
    map.set(word, 'learning')
  }

  return map
}

/** "Count: N" in the word panel — how often each base form occurs in the whole book. */
export function bookLemmaCounts(book: ReaderBook, lexicon: Lexicon | null): Map<string, number> {
  const counts = new Map<string, number>()

  for (const chapter of book.chapters) {
    for (const paragraph of chapter.paragraphs) {
      for (const sentence of paragraph.sentences) {
        for (const token of sentence.tokens) {
          const lemma = token.isWord ? resolveWord(token.text, EMPTY_STATUSES, lexicon)?.lemma : undefined

          if (lemma) {
            counts.set(lemma, (counts.get(lemma) ?? 0) + 1)
          }
        }
      }
    }
  }

  return counts
}
