import type { PromptForm, SessionVerb, VerbExercise } from '../api/client'

/**
 * Four right picks in a row pass a word. This mirrors `VerbScoring.PassStreak` on the server:
 * the browser runs the round, so it has to know when a word is done — the server stays the
 * authority on what the answer log adds up to.
 */
export const PassStreak = 4

/** One card on screen: a verb, the form its blank wants, and the exercise that asks for it. */
export interface Card {
  verb: SessionVerb
  promptForm: PromptForm
  exercise: VerbExercise
}

/** A free run's queue: a fixed list played in order. */
export interface Played {
  kind: 'queue'
  cards: Card[]
  index: number
}

/**
 * The `nth` showing of one form of a verb takes the next of that form's exercises, cycling —
 * so the verb's own sentence and the templates take turns rather than one sentence being
 * learned by heart. The server always sends at least one exercise per form.
 */
export function exerciseFor(verb: SessionVerb, form: PromptForm, nth: number): VerbExercise {
  const own = verb.exercises.filter((exercise) => exercise.form === form)

  return own[nth % own.length]
}

/** Where one word stands inside this session. */
interface DrillWord {
  verb: SessionVerb
  /** Consecutive right picks, seeded from what the server knew when the session started. */
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

/** True once every word of the session has four right picks in a row. */
export function isDone(state: DrillState): boolean {
  return state.current === null
}

/**
 * Records whether the pick on the card on screen was right and deals the next one. A verdict
 * after the round is over changes nothing.
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
  const promptForm = chosen.verb.formOrder[chosen.shows % chosen.verb.formOrder.length]

  return {
    words: state.words.map((word) =>
      word.verb.v1 === chosen.verb.v1
        ? { ...word, shows: word.shows + 1, shownAt: dealt }
        : word,
    ),
    dealt,
    current: {
      verb: chosen.verb,
      promptForm,
      exercise: exerciseFor(chosen.verb, promptForm, Math.floor(chosen.shows / chosen.verb.formOrder.length)),
    },
  }
}
