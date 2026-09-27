import { describe, expect, it } from 'vitest'
import type { SessionVerb } from '../api/client'
import { applyAnswer, createDrill, introQueue, isDone, PassStreak } from './session'

function verb(v1: string, overrides: Partial<SessionVerb> = {}): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 1,
    note: null,
    examples: { present: `I [${v1}].`, past: `I [${v1}-2].`, perfect: `I have [${v1}-3].` },
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
    fresh: true,
    ...overrides,
  }
}

describe('introQueue', () => {
  it('walks the words round-robin so a word is spaced apart from itself', () => {
    const cards = introQueue([verb('a'), verb('b'), verb('c')], 3)

    expect(cards).toHaveLength(9)
    expect(cards.map((c) => c.verb.v1)).toEqual(['a', 'b', 'c', 'a', 'b', 'c', 'a', 'b', 'c'])
  })

  it('takes the next form of the word own order on each pass', () => {
    const cards = introQueue([verb('a', { formOrder: ['v3', 'v1', 'v2'] })], 3)

    expect(cards.map((c) => c.promptForm)).toEqual(['v3', 'v1', 'v2'])
  })

  it('cycles the forms past the third pass', () => {
    const cards = introQueue([verb('a', { formOrder: ['v3', 'v1', 'v2'] })], 5)

    expect(cards.map((c) => c.promptForm)).toEqual(['v3', 'v1', 'v2', 'v3', 'v1'])
  })

  it('repeats a lone word, since there is nothing to space it against', () => {
    expect(introQueue([verb('a')], 3).map((c) => c.verb.v1)).toEqual(['a', 'a', 'a'])
  })

  it('is empty when no rounds were asked for', () => {
    expect(introQueue([verb('a')], 0)).toEqual([])
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

    // a, then b, then a again — each "know" puts the answered word one ahead.
    expect(state.current!.verb.v1).toBe('a')
    state = applyAnswer(state, true)
    expect(state.current!.verb.v1).toBe('b')
    state = applyAnswer(state, true)
    expect(state.current!.verb.v1).toBe('a')
  })

  it('takes a word out once it has four "I know" in a row', () => {
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
    let state = createDrill([verb('a', { streak: PassStreak - 1, answers: 5, fresh: false })])

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
