import { describe, expect, it } from 'vitest'
import type { SessionVerb, VerbExercise } from '../api/client'
import { applyAnswer, createDrill, exerciseFor, isDone, PassStreak } from './session'

function exercises(v1: string): VerbExercise[] {
  return (['v1', 'v2', 'v3'] as const).flatMap((form) => {
    const answer = form === 'v1' ? v1 : `${v1}-${form.slice(1)}`

    return [0, 1].map((n) => ({
      form,
      before: `S${n} `,
      after: '.',
      options: [answer, `${v1}ed`],
      answer,
    }))
  })
}

function verb(v1: string, overrides: Partial<SessionVerb> = {}): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 1,
    note: null,
    exercises: exercises(v1),
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
    ...overrides,
  }
}

describe('exerciseFor', () => {
  it('walks a form’s exercises in order and cycles', () => {
    const a = verb('a')

    expect(exerciseFor(a, 'v2', 0).before).toBe('S0 ')
    expect(exerciseFor(a, 'v2', 1).before).toBe('S1 ')
    expect(exerciseFor(a, 'v2', 2).before).toBe('S0 ')
    expect(exerciseFor(a, 'v2', 0).answer).toBe('a-2')
  })
})

describe('the drill round deals exercises', () => {
  it('gives each card the exercise of its form', () => {
    const state = createDrill([verb('a')])

    expect(state.current!.promptForm).toBe('v2')
    expect(state.current!.exercise.answer).toBe('a-2')
  })

  it('moves to the next exercise of a form once the form order has come round', () => {
    let state = createDrill([verb('a')])
    const seen: string[] = []

    for (let i = 0; i < 4; i++) {
      seen.push(`${state.current!.promptForm}:${state.current!.exercise.before}`)
      state = applyAnswer(state, false)
    }

    expect(seen).toEqual(['v2:S0 ', 'v1:S0 ', 'v3:S0 ', 'v2:S1 '])
  })
})

describe('the drill round', () => {
  it('opens on the first word it was given', () => {
    const state = createDrill([verb('a'), verb('b')])

    expect(state.current!.verb.v1).toBe('a')
    expect(isDone(state)).toBe(false)
  })

  it('does not serve the same word twice in a row while more than one is still in play', () => {
    let state = createDrill([verb('a'), verb('b'), verb('c')])

    for (let i = 0; i < 20; i++) {
      const shown = state.current!.verb.v1
      state = applyAnswer(state, i % 2 === 0)

      // Once this run of answers leaves only one word short of passing, that word is the only
      // candidate left and must repeat — exactly what "keeps serving a lone word to the end"
      // (below) checks on its own. The no-repeat guarantee only holds while a choice exists.
      const stillInPlay = state.words.filter((w) => w.streak < PassStreak).length

      if (state.current && stillInPlay > 1) {
        expect(state.current.verb.v1).not.toBe(shown)
      }
    }
  })

  it('serves the word with the lowest streak', () => {
    let state = createDrill([verb('a'), verb('b')])

    // a, then b, then a again — each right pick puts the answered word one ahead.
    expect(state.current!.verb.v1).toBe('a')
    state = applyAnswer(state, true)
    expect(state.current!.verb.v1).toBe('b')
    state = applyAnswer(state, true)
    expect(state.current!.verb.v1).toBe('a')
  })

  it('takes a word out once it has four right picks in a row', () => {
    let state = createDrill([verb('a'), verb('b')])

    for (let i = 0; i < PassStreak * 2; i++) {
      state = applyAnswer(state, true)
    }

    expect(isDone(state)).toBe(true)
    expect(state.current).toBeNull()
  })

  it('sends a word back to zero on a miss', () => {
    let state = createDrill([verb('a')])

    state = applyAnswer(state, true)
    state = applyAnswer(state, true)
    state = applyAnswer(state, false)

    expect(state.words[0].streak).toBe(0)
    expect(isDone(state)).toBe(false)
  })

  it('counts the streak a word arrived with', () => {
    let state = createDrill([verb('a', { streak: PassStreak - 1, answers: 5 })])

    state = applyAnswer(state, true)

    expect(isDone(state)).toBe(true)
  })

  it('keeps serving a lone word to the end', () => {
    let state = createDrill([verb('a')])

    for (let i = 0; i < PassStreak; i++) {
      expect(state.current!.verb.v1).toBe('a')
      state = applyAnswer(state, true)
    }

    expect(isDone(state)).toBe(true)
  })

  it('walks the form order across a word showings', () => {
    let state = createDrill([verb('a', { formOrder: ['v3', 'v1', 'v2'] })])
    const forms = [state.current!.promptForm]

    for (let i = 0; i < 3; i++) {
      state = applyAnswer(state, false)
      forms.push(state.current!.promptForm)
    }

    expect(forms).toEqual(['v3', 'v1', 'v2', 'v3'])
  })

  it('answers nothing when the round is over', () => {
    let state = createDrill([verb('a')])

    for (let i = 0; i < PassStreak; i++) {
      state = applyAnswer(state, true)
    }

    expect(applyAnswer(state, true)).toBe(state)
  })

  it('is done at once when it was given no words', () => {
    expect(isDone(createDrill([]))).toBe(true)
  })
})
