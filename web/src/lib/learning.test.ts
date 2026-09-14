import { describe, expect, it } from 'vitest'
import type { LearningProgress } from '../api/client'
import { chapterAction, learningPercent, learningSegments } from './learning'

const progress: LearningProgress = { notStarted: 49, boxes: [9, 5, 0, 3, 0], learned: 12, total: 78 }
const empty: LearningProgress = { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }

describe('learningPercent', () => {
  it('weights the boxes: (1·9 + 2·5 + 4·3 + 5·12) / (5·78) = 91/390 → 23%', () => {
    expect(learningPercent(progress)).toBe(23)
  })

  it('total 0 → 0, no NaN', () => {
    expect(learningPercent(empty)).toBe(0)
  })

  it('everything learned → 100; box 5 in progress counts as 100 too', () => {
    expect(learningPercent({ notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 7, total: 7 })).toBe(100)
    expect(learningPercent({ notStarted: 0, boxes: [0, 0, 0, 0, 4], learned: 0, total: 4 })).toBe(100)
  })

  it('nothing started → 0', () => {
    expect(learningPercent({ notStarted: 5, boxes: [0, 0, 0, 0, 0], learned: 0, total: 5 })).toBe(0)
  })
})

describe('learningSegments', () => {
  it('six segments in display order; box 5 is folded into learned', () => {
    const segments = learningSegments(progress)

    expect(segments.map((s) => s.key)).toEqual(['learned', 'box4', 'box3', 'box2', 'box1', 'new'])
    expect(segments.map((s) => s.label)).toEqual(['learned', 'box 4', 'box 3', 'box 2', 'box 1', 'not started'])
    expect(segments.map((s) => s.count)).toEqual([12, 3, 0, 5, 9, 49])
  })

  it('share is the fraction of total and sums to 1', () => {
    const segments = learningSegments(progress)

    expect(segments[0].share).toBeCloseTo(12 / 78)
    expect(segments.reduce((sum, s) => sum + s.share, 0)).toBeCloseTo(1)
  })

  it('total 0 → every share is 0', () => {
    expect(learningSegments(empty).every((s) => s.share === 0)).toBe(true)
  })
})

describe('chapterAction', () => {
  const base = { id: 1, order: 0, title: 'One', wordsCount: 100, sortedCount: 100, isStarred: false }
  const boxes = (...b: number[]) => ({ notStarted: 0, boxes: b, learned: 0, total: b.reduce((a, c) => a + c, 0) })

  it('new words left → exercise, whatever else is going on', () => {
    expect(chapterAction({ ...base, learnableCount: 4, learning: boxes(3, 0, 0, 0, 0), dueCount: 2, nextDueAt: null })).toEqual({
      kind: 'exercise',
      learnable: 4,
    })
  })

  it('no new words but some due → review', () => {
    expect(chapterAction({ ...base, learnableCount: 0, learning: boxes(3, 2, 0, 0, 0), dueCount: 2, nextDueAt: null })).toEqual({
      kind: 'review',
      due: 2,
    })
  })

  it('nothing due yet but words in progress → wait, with the count and the next due date', () => {
    expect(
      chapterAction({ ...base, learnableCount: 0, learning: boxes(3, 2, 1, 0, 0), dueCount: 0, nextDueAt: '2026-09-12T00:00:00Z' }),
    ).toEqual({ kind: 'wait', inProgress: 6, nextDueAt: '2026-09-12T00:00:00Z' })
  })

  it('nothing in progress at all → none', () => {
    expect(chapterAction({ ...base, learnableCount: 0, learning: boxes(0, 0, 0, 0, 0), dueCount: 0, nextDueAt: null })).toEqual({
      kind: 'none',
    })
  })
})
