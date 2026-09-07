import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { QueueWord, SortStatus } from '../api/client'
import { useSortingQueue } from './useSortingQueue'

const apiMock = vi.hoisted(() => ({
  getQueue: vi.fn(),
  mark: vi.fn(),
  getRecent: vi.fn(),
  undo: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (reason: unknown) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void

  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })

  return { promise, resolve, reject }
}

/// Фейковий сервер: словник із N слів і множина тих, чию позначку він уже записав.
let words: QueueWord[] = []
let recorded = new Set<number>()
let inFlight: { wordPairId: number; deferred: Deferred<null> }[] = []

function makeWords(count: number): QueueWord[] {
  return Array.from({ length: count }, (_, i) => ({
    wordPairId: i + 1,
    word: `w${i + 1}`,
    frequency: count - i,
  }))
}

/// Кожен mark зависає, доки тест його явно не «доставить» — саме це дає змогу
/// відтворити перегони між дозаливкою й позначкою, що ще летить.
function settleMark(index: number, outcome: 'ok' | 'fail') {
  const call = inFlight[index]

  if (outcome === 'ok') {
    recorded.add(call.wordPairId)
    call.deferred.resolve(null)
  } else {
    call.deferred.reject(new Error('boom'))
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  words = []
  recorded = new Set()
  inFlight = []

  apiMock.getQueue.mockImplementation(async () => {
    const remaining = words.filter((w) => !recorded.has(w.wordPairId))

    return {
      words: remaining,
      total: words.length,
      sorted: words.length - remaining.length,
      remaining: remaining.length,
    }
  })

  apiMock.mark.mockImplementation((wordPairId: number) => {
    const pending = deferred<null>()
    inFlight.push({ wordPairId, deferred: pending })
    return pending.promise
  })

  apiMock.getRecent.mockResolvedValue({ known: [], unknown: [] })
  apiMock.undo.mockResolvedValue(null)
})

async function renderHook<T>(hook: () => T) {
  const result = { current: undefined as T }

  function Probe() {
    result.current = hook()
    return null
  }

  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  await act(async () => {
    root.render(createElement(Probe))
  })

  return result
}

/// Макрозадача пропускає вперед усю чергу мікрозадач — тобто весь ланцюжок
/// mark → refill встигає доїхати до кінця, поки ми всередині act.
async function flush() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

describe('useSortingQueue', () => {
  async function arrange(count: number) {
    words = makeWords(count)
    const result = await renderHook(() => useSortingQueue({ dictionaryId: 1, chapterIds: null }))
    await flush()
    return result
  }

  async function mark(result: { current: ReturnType<typeof useSortingQueue> }, status: SortStatus) {
    await act(async () => {
      result.current.mark(status)
    })
  }

  it('дозаливка не воскрешає слово, чия позначка ще летить', async () => {
    // 21 слово = REFILL_AT + 1: перший же mark тягне дозаливку буфера.
    const result = await arrange(21)

    expect(result.current.current?.wordPairId).toBe(1)
    expect(result.current.sorted).toBe(0)

    await mark(result, 'known') // w1 полетів і завис
    await mark(result, 'known') // w2 позначили, поки w1 ще в дорозі

    expect(result.current.current?.wordPairId).toBe(3)
    expect(result.current.sorted).toBe(2)

    // w1 доїхав → його робота тягне refill. Сервер у відповіді ще нічого не знає
    // про w2, тож віддає його як непосортоване.
    settleMark(0, 'ok')
    await flush()

    expect(apiMock.getQueue).toHaveBeenCalledTimes(2)
    expect(inFlight.map((c) => c.wordPairId)).toEqual([1, 2])

    // Оптимістичний стан w2 пережив дозаливку: картка не відкотилась на w2,
    // прогрес не з'їхав назад на 1.
    expect(result.current.current?.wordPairId).toBe(3)
    expect(result.current.sorted).toBe(2)
    expect(result.current.known.map((w) => w.wordPairId)).toEqual([2, 1])
    expect(result.current.error).toBeNull()
  })

  it('невдалий mark відкочує лише своє слово, а не пізнішу позначку', async () => {
    // 25 слів: жоден із трьох mark не опускає буфер до REFILL_AT, тож дозаливки
    // тут немає й у грі лише відкат.
    const result = await arrange(25)

    await mark(result, 'known') // w1
    await mark(result, 'known') // w2, поки w1 ще летить

    expect(result.current.current?.wordPairId).toBe(3)
    expect(result.current.sorted).toBe(2)

    settleMark(0, 'fail')
    await flush()

    expect(result.current.current?.wordPairId).toBe(1)
    expect(result.current.sorted).toBe(1)
    expect(result.current.known.map((w) => w.wordPairId)).toEqual([2])
    expect(result.current.error).toContain('Не збереглося')
    expect(apiMock.getQueue).toHaveBeenCalledTimes(1)

    // І w2 не повернулося в буфер: після w1 наступна картка — w3.
    await mark(result, 'known')

    expect(result.current.current?.wordPairId).toBe(3)
  })
})
