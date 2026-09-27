import type { PromptForm, SessionVerb } from '../api/client'

/**
 * Four "I know" in a row passes a word. This mirrors `VerbScoring.PassStreak` on the server:
 * the browser runs the round, so it has to know when a word is done — the server stays the
 * authority on what the answer log adds up to.
 */
export const PassStreak = 4

/** One card on screen, in either round. */
export interface Card {
  verb: SessionVerb
  promptForm: PromptForm
}

/** The introduction round, and a free run's queue: a fixed list played in order. */
export interface Played {
  kind: 'intro' | 'queue'
  cards: Card[]
  index: number
}

/**
 * The introduction round's cards. The words go round-robin, so a word's repeats are spaced
 * apart by the others rather than piled up — the gap is what makes it stick. Each pass takes
 * the next form of the word's own order, so the word is met from all three sides; past the
 * third pass the order cycles. One word has nothing to space it against and simply repeats.
 */
export function introQueue(verbs: SessionVerb[], rounds: number): Card[] {
  const cards: Card[] = []

  for (let pass = 0; pass < rounds; pass++) {
    for (const verb of verbs) {
      cards.push({ verb, promptForm: verb.formOrder[pass % verb.formOrder.length] })
    }
  }

  return cards
}

/** Where one word stands inside this session. */
interface DrillWord {
  verb: SessionVerb
  /** Consecutive "I know", seeded from what the server knew when the session started. */
  streak: number
  /** How many showings it had, which is where its form order stands. */
  shows: number
  /** The deal this word was last shown on; smaller means longer ago. */
  shownAt: number
}

export interface DrillState {
  words: DrillWord[]
  /** Cards dealt so far — the clock `shownAt` is measured on. */
  dealt: number
  /** Null once every word has passed. */
  current: Card | null
}

export function createDrill(verbs: SessionVerb[]): DrillState {
  const words = verbs.map((verb) => ({ verb, streak: verb.streak, shows: 0, shownAt: 0 }))

  return deal({ words, dealt: 0, current: null }, null)
}

/** True once every word of the session has four "I know" in a row. */
export function isDone(state: DrillState): boolean {
  return state.current === null
}

/**
 * Records the learner's verdict on the card on screen and deals the next one. A verdict after
 * the round is over changes nothing.
 */
export function applyAnswer(state: DrillState, known: boolean): DrillState {
  const current = state.current

  if (!current) {
    return state
  }

  const words = state.words.map((word) =>
    word.verb.v1 === current.verb.v1 ? { ...word, streak: known ? word.streak + 1 : 0 } : word,
  )

  return deal({ ...state, words }, current.verb.v1)
}

/**
 * The next card: the word with the lowest streak, then the one shown longest ago, then the
 * order the server sent — skipping the word just answered unless it is the only one left, so
 * the same card never comes twice running.
 *
 * The server sorts the window by mastery; inside a session the streak is what the learner is
 * working towards, and going by it keeps the scoring formula out of the browser.
 */
function deal(state: DrillState, justAnswered: string | null): DrillState {
  const left = state.words.filter((word) => word.streak < PassStreak)

  if (left.length === 0) {
    return { ...state, current: null }
  }

  const pool = left.length > 1 ? left.filter((word) => word.verb.v1 !== justAnswered) : left

  // Array.prototype.sort is stable, so words alike in both keys keep the server's order.
  const chosen = [...pool].sort((a, b) => a.streak - b.streak || a.shownAt - b.shownAt)[0]
  const dealt = state.dealt + 1

  return {
    words: state.words.map((word) =>
      word.verb.v1 === chosen.verb.v1
        ? { ...word, shows: word.shows + 1, shownAt: dealt }
        : word,
    ),
    dealt,
    current: {
      verb: chosen.verb,
      promptForm: chosen.verb.formOrder[chosen.shows % chosen.verb.formOrder.length],
    },
  }
}
