import { describe, expect, it } from 'vitest'
import type { LearningProgress } from '../api/client'
import { learningPercent, learningSegments } from './learning'

const progress: LearningProgress = { notStarted: 49, boxes: [9, 5, 0, 3, 0], learned: 12, total: 78 }
const empty: LearningProgress = { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }

describe('learningPercent', () => {
  it('зважує бокси: (1·9 + 2·5 + 4·3 + 5·12) / (5·78) = 91/390 → 23%', () => {
    expect(learningPercent(progress)).toBe(23)
  })

  it('total 0 → 0, без NaN', () => {
    expect(learningPercent(empty)).toBe(0)
  })

  it('усе вивчено → 100; бокс 5 у процесі теж рахується як 100', () => {
    expect(learningPercent({ notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 7, total: 7 })).toBe(100)
    expect(learningPercent({ notStarted: 0, boxes: [0, 0, 0, 0, 4], learned: 0, total: 4 })).toBe(100)
  })

  it('нічого не почато → 0', () => {
    expect(learningPercent({ notStarted: 5, boxes: [0, 0, 0, 0, 0], learned: 0, total: 5 })).toBe(0)
  })
})

describe('learningSegments', () => {
  it('шість сегментів у порядку показу; бокс 5 складено з вивченими', () => {
    const segments = learningSegments(progress)

    expect(segments.map((s) => s.key)).toEqual(['learned', 'box4', 'box3', 'box2', 'box1', 'new'])
    expect(segments.map((s) => s.label)).toEqual(['вивчено', 'бокс 4', 'бокс 3', 'бокс 2', 'бокс 1', 'не почато'])
    expect(segments.map((s) => s.count)).toEqual([12, 3, 0, 5, 9, 49])
  })

  it('share — частка від total, у сумі 1', () => {
    const segments = learningSegments(progress)

    expect(segments[0].share).toBeCloseTo(12 / 78)
    expect(segments.reduce((sum, s) => sum + s.share, 0)).toBeCloseTo(1)
  })

  it('total 0 → усі share 0', () => {
    expect(learningSegments(empty).every((s) => s.share === 0)).toBe(true)
  })
})
