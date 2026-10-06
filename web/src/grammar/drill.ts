import type { GrammarExercise } from '../api/client'

const BLANK = '___'

export type Random = () => number

/** One exercise as the drill shows it: the sentence split at its blank, the options shuffled. */
export interface DrillCard {
  before: string
  after: string
  options: string[]
  answer: string
  why: string
}

/** A topic's round. Pure data — the screen holds it in state and swaps it on every step. */
export interface Drill {
  cards: DrillCard[]
  index: number
  /** The option picked on the current card; null until a pick. */
  picked: string | null
  /** The cards answered wrong, in the order they came. */
  missed: DrillCard[]
}

/** Fisher–Yates on a copy. */
export function shuffle<T>(items: readonly T[], random: Random): T[] {
  const result = [...items]

  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1))
    ;[result[i], result[j]] = [result[j], result[i]]
  }

  return result
}

function toCard(exercise: GrammarExercise, random: Random): DrillCard {
  const at = exercise.sentence.indexOf(BLANK)

  return {
    before: exercise.sentence.slice(0, at),
    after: exercise.sentence.slice(at + BLANK.length),
    options: shuffle(exercise.options, random),
    answer: exercise.answer,
    why: exercise.why,
  }
}

/** A fresh round: the exercises and each one's options in a new order. */
export function startDrill(exercises: readonly GrammarExercise[], random: Random = Math.random): Drill {
  return {
    cards: shuffle(exercises, random).map((exercise) => toCard(exercise, random)),
    index: 0,
    picked: null,
    missed: [],
  }
}

export function currentCard(drill: Drill): DrillCard | null {
  return drill.cards[drill.index] ?? null
}

/** The first pick on a card counts; any later one is ignored. */
export function pick(drill: Drill, option: string): Drill {
  const card = currentCard(drill)

  if (card === null || drill.picked !== null) {
    return drill
  }

  return {
    ...drill,
    picked: option,
    missed: option === card.answer ? drill.missed : [...drill.missed, card],
  }
}

export function next(drill: Drill): Drill {
  return { ...drill, index: drill.index + 1, picked: null }
}

export function isFinished(drill: Drill): boolean {
  return drill.index >= drill.cards.length
}

export function rightCount(drill: Drill): number {
  return drill.cards.length - drill.missed.length
}
