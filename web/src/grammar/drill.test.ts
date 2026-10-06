import { describe, expect, it } from 'vitest'
import type { GrammarExercise } from '../api/client'
import { currentCard, isFinished, next, pick, rightCount, shuffle, startDrill } from './drill'

/** Fisher–Yates with a random just under 1 swaps every item with itself: order kept. */
const keepOrder = () => 0.9999

const exercises: GrammarExercise[] = [
  { sentence: 'My parents ___ at home.', options: ['am', 'is', 'are'], answer: 'are', why: 'they → are' },
  { sentence: '___ there a park near here?', options: ['Is', 'Are'], answer: 'Is', why: 'one park' },
]

describe('shuffle', () => {
  it('returns a permutation and leaves the input alone', () => {
    const items = [1, 2, 3, 4, 5]
    const shuffled = shuffle(items, Math.random)

    expect([...shuffled].sort()).toEqual([1, 2, 3, 4, 5])
    expect(items).toEqual([1, 2, 3, 4, 5])
  })

  it('reorders when the random says so', () => {
    expect(shuffle([1, 2, 3], () => 0)).toEqual([2, 3, 1])
  })
})

describe('startDrill', () => {
  it('splits each sentence at its blank', () => {
    const drill = startDrill(exercises, keepOrder)

    expect(drill.cards[0]).toMatchObject({ before: 'My parents ', after: ' at home.', answer: 'are' })
  })

  it('handles a blank at the start of the sentence', () => {
    const drill = startDrill(exercises, keepOrder)

    expect(drill.cards[1]).toMatchObject({ before: '', after: ' there a park near here?' })
  })

  it('keeps every exercise and every option', () => {
    const drill = startDrill(exercises, Math.random)

    expect(drill.cards.map((c) => c.answer).sort()).toEqual(['Is', 'are'])
    expect(drill.cards.find((c) => c.answer === 'are')!.options.sort()).toEqual(['am', 'are', 'is'])
    expect(drill).toMatchObject({ index: 0, picked: null, missed: [] })
  })
})

describe('a round', () => {
  it('a right pick misses nothing', () => {
    const drill = pick(startDrill(exercises, keepOrder), 'are')

    expect(drill.picked).toBe('are')
    expect(drill.missed).toEqual([])
  })

  it('a wrong pick records the card as missed', () => {
    const start = startDrill(exercises, keepOrder)
    const drill = pick(start, 'is')

    expect(drill.missed).toEqual([start.cards[0]])
  })

  it('ignores a second pick on the same card', () => {
    const once = pick(startDrill(exercises, keepOrder), 'are')

    expect(pick(once, 'is')).toBe(once)
  })

  it('next moves on and clears the pick, and finishes after the last card', () => {
    let drill = pick(startDrill(exercises, keepOrder), 'is')
    drill = next(drill)

    expect(drill.index).toBe(1)
    expect(drill.picked).toBeNull()
    expect(currentCard(drill)?.answer).toBe('Is')
    expect(isFinished(drill)).toBe(false)

    drill = next(pick(drill, 'Is'))

    expect(isFinished(drill)).toBe(true)
    expect(currentCard(drill)).toBeNull()
    expect(rightCount(drill)).toBe(1)
  })
})
