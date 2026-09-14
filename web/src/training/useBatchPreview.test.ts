import { describe, expect, it } from 'vitest'
import type { BatchCandidate } from '../api/client'
import { reconcileRows, type Row } from './useBatchPreview'

const c = (id: number): BatchCandidate => ({ wordPairId: id, word: `w${id}`, translation: `t${id}`, frequency: 100 - id })
const ids = (rows: Row[]) => rows.map((r) => r.candidate.wordPairId)
const struck = (rows: Row[]) => rows.filter((r) => r.struck).map((r) => r.candidate.wordPairId)

describe('reconcileRows', () => {
  it('first render: the first batchSize candidates, none struck', () => {
    const rows = reconcileRows([], [c(1), c(2), c(3), c(4)], 3, new Set())

    expect(ids(rows)).toEqual([1, 2, 3])
    expect(struck(rows)).toEqual([])
  })

  it('a struck word stays in place, the replacement is appended at the bottom', () => {
    const prev = reconcileRows([], [c(1), c(2), c(3), c(4), c(5)], 3, new Set())

    const rows = reconcileRows(prev, [c(1), c(3), c(4), c(5)], 3, new Set([2]))

    expect(ids(rows)).toEqual([1, 2, 3, 4])
    expect(struck(rows)).toEqual([2])
  })

  it('bringing back clears the strike; the surplus active row at the bottom drops', () => {
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

  it('a smaller batch size trims active rows but not struck ones', () => {
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

  it('a bigger batch size appends new rows at the bottom without reordering the old ones', () => {
    const prev = reconcileRows([], [c(1), c(2), c(3), c(4), c(5)], 2, new Set())

    const rows = reconcileRows(prev, [c(1), c(2), c(3), c(4), c(5)], 4, new Set())

    expect(ids(rows)).toEqual([1, 2, 3, 4])
  })
})
