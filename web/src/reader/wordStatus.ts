import { adjective, noun, verb } from 'wink-lemmatizer'
import { cleanWord, isRejected } from '../fb2/tokenize'
import type { ReaderBook } from './readerBook'

export type WordStatus = 'new' | 'learning' | 'known'

/** The learner's standing by base form; a form missing here is new. From GET /api/reader/word-statuses. */
export type KnownStatuses = ReadonlyMap<string, 'learning' | 'known'>

export const EMPTY_STATUSES: KnownStatuses = new Map()

/** status null: a function word (the, of, don't) — tappable, never highlighted. */
export interface ResolvedWord {
  lemma: string
  status: WordStatus | null
}

interface Analysis {
  word: string
  /** null for a function word; otherwise the form itself first, then its base-form guesses. */
  candidates: string[] | null
}

// A book repeats its words endlessly: analyse each spelling once per page load.
const analyses = new Map<string, Analysis | null>()

function analyse(token: string): Analysis | null {
  const cached = analyses.get(token)

  if (cached !== undefined) {
    return cached
  }

  const word = cleanWord(token)

  // Only letters, apostrophes and hyphens make a word the API accepts (WordText on the server).
  const analysis: Analysis | null = !/^[a-z'-]+$/.test(word) || !/[a-z]/.test(word)
    ? null
    : {
        word,
        candidates: isRejected(word) ? null : [...new Set([word, verb(word), noun(word), adjective(word)])],
      }

  analyses.set(token, analysis)
  return analysis
}

/**
 * A token of the text → its base form and the learner's standing on it. No part-of-speech
 * tagging (compromise is too slow on the main thread, and workers do not start inside
 * Telegram): every base-form guess is tried and the first one the learner has a standing on
 * wins. A new word is shown under its first guess that differs from the form itself. Now and
 * then a lemma is guessed wrong — accepted for highlighting.
 */
export function resolveWord(token: string, statuses: KnownStatuses): ResolvedWord | null {
  const analysis = analyse(token)

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

  return { lemma: analysis.candidates.find((c) => c !== analysis.word) ?? analysis.word, status: 'new' }
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
export function bookLemmaCounts(book: ReaderBook): Map<string, number> {
  const counts = new Map<string, number>()

  for (const chapter of book.chapters) {
    for (const paragraph of chapter.paragraphs) {
      for (const sentence of paragraph.sentences) {
        for (const token of sentence.tokens) {
          const lemma = token.isWord ? resolveWord(token.text, EMPTY_STATUSES)?.lemma : undefined

          if (lemma) {
            counts.set(lemma, (counts.get(lemma) ?? 0) + 1)
          }
        }
      }
    }
  }

  return counts
}
