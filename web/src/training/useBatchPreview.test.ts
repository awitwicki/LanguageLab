import { describe, expect, it } from 'vitest'
import type { BatchCandidate } from '../api/client'
import { reconcileRows, type Row } from './useBatchPreview'

const c = (id: number): BatchCandidate => ({ wordPairId: id, word: `w${id}`, translation: `t${id}`, frequency: 100 - id })
const ids = (rows: Row[]) => rows.map((r) => r.candidate.wordPairId)
const struck = (rows: Row[]) => rows.filter((r) => r.struck).map((r) => r.candidate.wordPairId)

describe('reconcileRows', () => {
  it('перший рендер: перші batchSize кандидатів, ніхто не викреслений', () => {
    const rows = reconcileRows([], [c(1), c(2), c(3), c(4)], 3, new Set())

    expect(ids(rows)).toEqual([1, 2, 3])
    expect(struck(rows)).toEqual([])
  })

  it('викреслене слово лишається на місці, заміна дописується знизу', () => {
    const prev = reconcileRows([], [c(1), c(2), c(3), c(4), c(5)], 3, new Set())

    const rows = reconcileRows(prev, [c(1), c(3), c(4), c(5)], 3, new Set([2]))

    expect(ids(rows)).toEqual([1, 2, 3, 4])
    expect(struck(rows)).toEqual([2])
  })

  it('повернення знімає викреслення; зайвий активний знизу випадає', () => {
    const prev: Row[] = [
      { candidate: c(1), struck: false },
      { candidate: c(2), struck: true },
      { candidate: c(3), struck: false },
      { candidate: c(4), struck: false },
    ]

    const rows = reconcileRows(prev, [c(1), c(2), c(3), c(4), c(5)], 3, new Set())

    expect(ids(rows)).toEqual([1, 2, 3])
    expect(struck(rows)).toEqual([])
  })

  it('менший розмір батча обрізає активні, але не викреслені', () => {
    const prev: Row[] = [
      { candidate: c(1), struck: false },
      { candidate: c(2), struck: true },
      { candidate: c(3), struck: false },
      { candidate: c(4), struck: false },
    ]

    const rows = reconcileRows(prev, [c(1), c(3), c(4), c(5)], 2, new Set([2]))

    expect(ids(rows)).toEqual([1, 2, 3])
    expect(struck(rows)).toEqual([2])
  })

  it('більший розмір батча дописує нових знизу, порядок старих не рухає', () => {
    const prev = reconcileRows([], [c(1), c(2), c(3), c(4), c(5)], 2, new Set())

    const rows = reconcileRows(prev, [c(1), c(2), c(3), c(4), c(5)], 4, new Set())

    expect(ids(rows)).toEqual([1, 2, 3, 4])
  })
})
