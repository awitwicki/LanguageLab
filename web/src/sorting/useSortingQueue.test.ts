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

/// A fake server: a dictionary of N words and the set of those whose mark it has already recorded.
let words: QueueWord[] = []
let recorded = new Set<number>()
let inFlight: { wordPairId: number; deferred: Deferred<null> }[] = []

function makeWords(count: number): QueueWord[] {
  return Array.from({ length: count }, (_, i) => ({
    wordPairId: i + 1,
    word: `w${i + 1}`,
    translation: `t${i + 1}`,
    frequency: count - i,
  }))
}

/// Every mark hangs until the test explicitly "delivers" it — that is what makes it
/// possible to reproduce the race between a refill and a mark still in flight.
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

/// A macrotask lets the whole microtask queue run ahead — i.e. the entire
/// mark → refill chain gets to the end while we are inside act.
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

  it('a refill does not resurrect a word whose mark is still in flight', async () => {
    // 21 words = REFILL_AT + 1: the very first mark triggers a buffer refill.
    const result = await arrange(21)

    expect(result.current.current?.wordPairId).toBe(1)
    expect(result.current.sorted).toBe(0)

    await mark(result, 'known') // w1 sent and hanging
    await mark(result, 'known') // w2 marked while w1 is still in flight

    expect(result.current.current?.wordPairId).toBe(3)
    expect(result.current.sorted).toBe(2)

    // w1 arrived → its completion triggers a refill. The server's response knows
    // nothing about w2 yet, so it returns it as unsorted.
    settleMark(0, 'ok')
    await flush()

    expect(apiMock.getQueue).toHaveBeenCalledTimes(2)
    expect(inFlight.map((c) => c.wordPairId)).toEqual([1, 2])

    // w2's optimistic state survived the refill: the card did not roll back to w2,
    // the progress did not slip back by 1.
    expect(result.current.current?.wordPairId).toBe(3)
    expect(result.current.sorted).toBe(2)
    expect(result.current.known.map((w) => w.wordPairId)).toEqual([2, 1])
    expect(result.current.error).toBeNull()
  })

  it('a failed mark rolls back only its own word, not a later one', async () => {
    // 25 words: none of the three marks drops the buffer to REFILL_AT, so there is
    // no refill here and only the rollback is in play.
    const result = await arrange(25)

    await mark(result, 'known') // w1
    await mark(result, 'known') // w2, while w1 is still in flight

    expect(result.current.current?.wordPairId).toBe(3)
    expect(result.current.sorted).toBe(2)

    settleMark(0, 'fail')
    await flush()

    expect(result.current.current?.wordPairId).toBe(1)
    expect(result.current.sorted).toBe(1)
    expect(result.current.known.map((w) => w.wordPairId)).toEqual([2])
    expect(result.current.error).toContain('Could not save')
    expect(apiMock.getQueue).toHaveBeenCalledTimes(1)

    // And w2 did not return to the buffer: after w1 the next card is w3.
    await mark(result, 'known')

    expect(result.current.current?.wordPairId).toBe(3)
  })

  it('loaded turns true after the first queue load, even an empty one', async () => {
    words = makeWords(0)
    const result = await renderHook(() => useSortingQueue({ dictionaryId: 1, chapterIds: null }))
    await flush()

    // An empty dictionary: total=0, current=null — but that is "loaded and empty",
    // not "still loading". The screen renders exactly that difference differently.
    expect(result.current.loaded).toBe(true)
    expect(result.current.current).toBeNull()
    expect(result.current.total).toBe(0)
  })
})
