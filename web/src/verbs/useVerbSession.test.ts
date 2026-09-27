import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DrillQuery, SessionVerb, VerbSession } from '../api/client'
import { RetryDelayMs } from './outbox'
import { readSavedSession, writeSavedSession } from './savedSession'
import { PassStreak } from './session'
import { useVerbSession } from './useVerbSession'

const apiMock = vi.hoisted(() => ({ getVerbSession: vi.fn(), postVerbAnswers: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

function verb(v1: string, overrides: Partial<SessionVerb> = {}): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 4,
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

function batch(verbs: SessionVerb[]): VerbSession {
  return { mode: 'batch', verbs, queue: null, scope: { passed: 2, total: 27 } }
}

function free(verbs: SessionVerb[], length: number): VerbSession {
  return {
    mode: 'free',
    verbs,
    queue: Array.from({ length }, (_, i) => ({
      verb: verbs[i % verbs.length].v1,
      promptForm: 'v2' as const,
    })),
    scope: { passed: 2, total: 27 },
  }
}

// Held stable across renders, exactly as `App.tsx`'s route state holds `query` for the real
// screen — `renderHook`'s wrapper closure runs again on every render of `Probe`, so a query
// object literal written inline there would be a fresh reference each time, defeating the
// `useEffect` dependency the hook relies on to fetch the session exactly once.
const BatchQuery: DrillQuery = { mode: 'batch', group: 4 }
const FreeQuery: DrillQuery = { mode: 'free', group: 4, scope: 'stage' }

async function renderHook<T>(hook: () => T) {
  const result = { current: undefined as T }

  function Probe() {
    result.current = hook()
    return null
  }

  const container = document.createElement('div')
  document.body.appendChild(container)

  await act(async () => {
    createRoot(container).render(createElement(Probe))
  })

  return result
}

/// A macrotask lets the whole microtask chain — fetch, then the state it sets — run to the end.
async function flush() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  apiMock.postVerbAnswers.mockResolvedValue({ results: [] })
})

describe('useVerbSession, ordinary training', () => {
  async function arrange(verbs = [verb('a'), verb('b'), verb('c')]) {
    apiMock.getVerbSession.mockResolvedValue(batch(verbs))
    const result = await renderHook(() => useVerbSession(BatchQuery))
    await flush()

    return result
  }

  it('opens on the start screen with the words on offer', async () => {
    const result = await arrange()

    expect(result.current.phase).toBe('start')
    expect(result.current.offer.map((v) => v.v1)).toEqual(['a', 'b', 'c'])
    expect(result.current.settings).toEqual({ words: 3, rounds: 3 })
    expect(result.current.card).toBeNull()
  })

  it('reports a finished stage instead of a start screen', async () => {
    apiMock.getVerbSession.mockResolvedValue(null)
    const result = await renderHook(() => useVerbSession(BatchQuery))
    await flush()

    expect(result.current.phase).toBe('finished')
  })

  it('plays the introduction round it was asked for', async () => {
    const result = await arrange()

    await act(async () => result.current.setWords(2))
    await act(async () => result.current.setRounds(2))
    await act(async () => result.current.startIntro())

    expect(result.current.phase).toBe('intro')
    expect(result.current.intro).toEqual({ step: 1, total: 4 })
    expect(result.current.card!.verb.v1).toBe('a')

    await act(async () => result.current.next())
    expect(result.current.intro).toEqual({ step: 2, total: 4 })
    expect(result.current.card!.verb.v1).toBe('b')

    // Card 3 (step 3/4) and card 4 (step 4/4) each still take their own Next; a fourth Next,
    // clicked while card 4 is on screen, is what actually crosses past the last card.
    await act(async () => result.current.next())
    await act(async () => result.current.next())
    await act(async () => result.current.next())

    // The last Next of the introduction leads into the drill round.
    expect(result.current.phase).toBe('drill')
    expect(result.current.intro).toBeNull()
    expect(apiMock.postVerbAnswers).not.toHaveBeenCalled()
  })

  it('goes straight to the drill round when the introduction is skipped', async () => {
    const result = await arrange()

    await act(async () => result.current.skipIntro())

    expect(result.current.phase).toBe('drill')
    expect(result.current.card!.verb.v1).toBe('a')
  })

  it('advances on "I know" without revealing anything', async () => {
    const result = await arrange()
    await act(async () => result.current.skipIntro())

    await act(async () => result.current.answer(true))

    expect(result.current.revealed).toBe(false)
    expect(result.current.card!.verb.v1).not.toBe('a')
    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([
      expect.objectContaining({ verb: 'a', known: true, mode: 'batch', group: 4 }),
    ])
  })

  it('holds the card revealed on "I don\'t know" until Next', async () => {
    const result = await arrange()
    await act(async () => result.current.skipIntro())

    await act(async () => result.current.answer(false))

    expect(result.current.revealed).toBe(true)
    expect(result.current.card!.verb.v1).toBe('a')

    await act(async () => result.current.next())

    expect(result.current.revealed).toBe(false)
    expect(result.current.card!.verb.v1).not.toBe('a')
  })

  /// Review focus 4: a double tap, or a keypress landing on a card already answered.
  it('records one answer however often the verdict is pressed', async () => {
    const result = await arrange()
    await act(async () => result.current.skipIntro())

    await act(async () => {
      result.current.answer(false)
      result.current.answer(false)
      result.current.answer(true)
    })

    expect(apiMock.postVerbAnswers).toHaveBeenCalledTimes(1)
    expect(apiMock.postVerbAnswers.mock.calls[0][0]).toHaveLength(1)
  })

  /// Review focus 4, the "I know" path: this instantly advances by calling advance() inside
  /// the same answer() call — a second verdict reaching the same still-stale card, in the same
  /// synchronous tick, must still be blocked, exactly as the "I don't know" path already is above.
  it('records one answer when "I know" is pressed twice in the same tick', async () => {
    const result = await arrange()
    await act(async () => result.current.skipIntro())

    await act(async () => {
      result.current.answer(true)
      result.current.answer(true)
    })

    expect(apiMock.postVerbAnswers).toHaveBeenCalledTimes(1)
    expect(apiMock.postVerbAnswers.mock.calls[0][0]).toHaveLength(1)
  })

  it('counts a word that passes into the scope progress', async () => {
    const result = await arrange([verb('a')])
    await act(async () => result.current.skipIntro())

    expect(result.current.scope).toEqual({ passed: 2, total: 27 })

    for (let i = 0; i < PassStreak; i++) {
      await act(async () => result.current.answer(true))
    }

    expect(result.current.phase).toBe('done')
    expect(result.current.scope).toEqual({ passed: 3, total: 27 })
  })

  /// Review focus 3: the next window is computed from what the server has recorded.
  it('drains the outbox before asking for the next words', async () => {
    const result = await arrange([verb('a')])
    await act(async () => result.current.skipIntro())

    for (let i = 0; i < PassStreak; i++) {
      await act(async () => result.current.answer(true))
    }

    await flush()
    apiMock.getVerbSession.mockClear()
    apiMock.getVerbSession.mockResolvedValue(batch([verb('d')]))

    await act(async () => result.current.nextWords())
    await flush()

    expect(apiMock.postVerbAnswers).toHaveBeenCalled()
    expect(apiMock.getVerbSession).toHaveBeenCalledTimes(1)
    expect(result.current.phase).toBe('start')
    expect(result.current.offer.map((v) => v.v1)).toEqual(['d'])
  })

  /// Review focus 3: a failed flush must not fetch a window built on stale state. Fake timers
  /// here, because the outbox waits RetryDelayMs before its one retry — and `flush()` above
  /// cannot be used while they are on, since it waits on a real setTimeout.
  it('refuses to fetch the next words while answers are stuck', async () => {
    vi.useFakeTimers()

    try {
      apiMock.postVerbAnswers.mockRejectedValue(new Error('offline'))
      apiMock.getVerbSession.mockResolvedValue(batch([verb('a')]))

      const result = await renderHook(() => useVerbSession(BatchQuery))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })

      await act(async () => result.current.skipIntro())

      for (let i = 0; i < PassStreak; i++) {
        await act(async () => result.current.answer(true))
      }

      // Past the retry: the outbox has given up for now and kept the answers.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(RetryDelayMs * 2)
      })

      expect(result.current.stuck).toBe(true)

      apiMock.getVerbSession.mockClear()
      await act(async () => result.current.nextWords())
      await act(async () => {
        await vi.advanceTimersByTimeAsync(RetryDelayMs * 2)
      })

      expect(apiMock.getVerbSession).not.toHaveBeenCalled()
      expect(result.current.error).toContain('offline')
      expect(result.current.phase).toBe('done')
    } finally {
      vi.useRealTimers()
    }
  })

  it('surfaces a failed session fetch', async () => {
    apiMock.getVerbSession.mockRejectedValue(new Error('no network'))
    const result = await renderHook(() => useVerbSession(BatchQuery))
    await flush()

    expect(result.current.error).toContain('no network')
  })
})

describe('useVerbSession, a free run', () => {
  it('plays the queue the server drew, in order', async () => {
    apiMock.getVerbSession.mockResolvedValue(free([verb('a'), verb('b')], 20))
    const result = await renderHook(() => useVerbSession(FreeQuery))
    await flush()

    expect(result.current.phase).toBe('drill')
    expect(result.current.card!.verb.v1).toBe('a')

    await act(async () => result.current.answer(true))

    expect(result.current.card!.verb.v1).toBe('b')
  })

  it('refills before the queue runs out', async () => {
    apiMock.getVerbSession.mockResolvedValue(free([verb('a')], 6))
    const result = await renderHook(() => useVerbSession(FreeQuery))
    await flush()

    expect(apiMock.getVerbSession).toHaveBeenCalledTimes(1)

    // Six cards and a refill at five left, so the first answer already crosses the line; the
    // chunk it fetches lands before the second one, which therefore asks for nothing.
    await act(async () => result.current.answer(true))
    await act(async () => result.current.answer(true))
    await flush()

    expect(apiMock.getVerbSession).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toBe('drill')
  })

  /// Review focus 5: a fast clicker on a slow connection.
  it('waits rather than blanking when the queue empties first', async () => {
    let release: (session: VerbSession) => void = () => {}
    apiMock.getVerbSession
      .mockResolvedValueOnce(free([verb('a')], 2))
      .mockImplementationOnce(() => new Promise<VerbSession>((resolve) => (release = resolve)))

    const result = await renderHook(() => useVerbSession(FreeQuery))
    await flush()

    await act(async () => result.current.answer(true))
    await act(async () => result.current.answer(true))

    expect(result.current.card).toBeNull()
    expect(result.current.busy).toBe(true)
    expect(result.current.error).toBeNull()

    // A click with no card on screen must do nothing at all.
    await act(async () => result.current.answer(true))
    expect(apiMock.postVerbAnswers.mock.calls.flatMap((call) => call[0])).toHaveLength(2)

    await act(async () => {
      release(free([verb('b')], 20))
    })
    await flush()

    expect(result.current.card!.verb.v1).toBe('b')
    expect(result.current.busy).toBe(false)
  })
})

describe('useVerbSession, picking up after a reload', () => {
  const UserId = 7
  const Title = 'All three forms differ'

  async function arrange(session: VerbSession | null, query = BatchQuery) {
    apiMock.getVerbSession.mockResolvedValue(session)
    const result = await renderHook(() => useVerbSession(query, { userId: UserId, title: Title }))
    await flush()

    return result
  }

  async function resume(query = BatchQuery) {
    const saved = readSavedSession(UserId)
    expect(saved).not.toBeNull()
    apiMock.getVerbSession.mockClear()
    apiMock.postVerbAnswers.mockClear()

    const result = await renderHook(() => useVerbSession(query, { userId: UserId, title: Title, resume: saved }))
    await flush()

    return result
  }

  it('saves nothing on the start screen', async () => {
    await arrange(batch([verb('a'), verb('b'), verb('c')]))

    expect(readSavedSession(UserId)).toBeNull()
  })

  it('saves the drill round as it is played', async () => {
    const result = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => result.current.skipIntro())
    await act(async () => result.current.answer(true))
    await flush()

    const saved = readSavedSession(UserId)!

    expect(saved.phase).toBe('drill')
    expect(saved.title).toBe(Title)
    expect(saved.query).toEqual(BatchQuery)
    expect(saved.drill!.current!.verb.v1).toBe(result.current.card!.verb.v1)
    // Sent already, so a resume must not post it again.
    expect(saved.pending).toEqual([])
  })

  /// A miss is recorded the moment it is pressed; a reload while its answer is on screen must
  /// not deal the same card to be answered twice.
  it('saves the card after a missed one while its answer is on screen', async () => {
    const result = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => result.current.skipIntro())
    await act(async () => result.current.answer(false))

    expect(result.current.card!.verb.v1).toBe('a')
    expect(readSavedSession(UserId)!.drill!.current!.verb.v1).not.toBe('a')
  })

  it('resumes the drill round where it stopped without asking the server for words', async () => {
    const first = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => first.current.skipIntro())
    await act(async () => first.current.answer(true))
    await act(async () => first.current.answer(false))
    const next = readSavedSession(UserId)!.drill!.current!.verb.v1

    const result = await resume()

    expect(apiMock.getVerbSession).not.toHaveBeenCalled()
    expect(result.current.phase).toBe('drill')
    expect(result.current.revealed).toBe(false)
    expect(result.current.card!.verb.v1).toBe(next)
    expect(result.current.scope).toEqual({ passed: 2, total: 27 })
  })

  it('resumes the introduction on its next card and then drills the words taken', async () => {
    const first = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => first.current.setWords(2))
    await act(async () => first.current.setRounds(1))
    await act(async () => first.current.startIntro())
    await act(async () => first.current.next())

    const result = await resume()

    expect(result.current.phase).toBe('intro')
    expect(result.current.intro).toEqual({ step: 2, total: 2 })
    expect(result.current.card!.verb.v1).toBe('b')

    await act(async () => result.current.next())

    expect(result.current.phase).toBe('drill')

    // Only the two words taken are drilled: four "I know" each pass them both.
    for (let i = 0; i < PassStreak * 2; i++) {
      await act(async () => result.current.answer(true))
    }

    expect(result.current.phase).toBe('done')
  })

  it('resumes a free run on its next card', async () => {
    const first = await arrange(free([verb('a'), verb('b'), verb('c')], 20), FreeQuery)
    await act(async () => first.current.answer(true))
    await act(async () => first.current.answer(false))

    const result = await resume(FreeQuery)

    expect(result.current.phase).toBe('drill')
    expect(result.current.card!.verb.v1).toBe('c')
  })

  it('sends the answers that had not left before the reload', async () => {
    apiMock.postVerbAnswers.mockRejectedValue(new Error('offline'))
    vi.useFakeTimers()

    try {
      apiMock.getVerbSession.mockResolvedValue(batch([verb('a'), verb('b'), verb('c')]))
      const first = await renderHook(() => useVerbSession(BatchQuery, { userId: UserId, title: Title }))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      await act(async () => first.current.skipIntro())
      await act(async () => first.current.answer(true))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(RetryDelayMs * 2)
      })
    } finally {
      vi.useRealTimers()
    }

    expect(readSavedSession(UserId)!.pending).toHaveLength(1)

    apiMock.postVerbAnswers.mockResolvedValue({ results: [] })
    await resume()

    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([expect.objectContaining({ verb: 'a', known: true })])
    expect(readSavedSession(UserId)!.pending).toEqual([])
  })

  it('forgets the round once it has passed and its answers are in', async () => {
    const result = await arrange(batch([verb('a')]))
    await act(async () => result.current.skipIntro())

    for (let i = 0; i < PassStreak; i++) {
      await act(async () => result.current.answer(true))
    }

    await flush()

    expect(result.current.phase).toBe('done')
    expect(readSavedSession(UserId)).toBeNull()
  })

  it('forgets the round the learner walked away from', async () => {
    const result = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => result.current.skipIntro())
    await act(async () => result.current.answer(true))

    await act(async () => result.current.discard())
    await flush()

    expect(readSavedSession(UserId)).toBeNull()
  })

  /// A fresh round started over a saved one takes over what the saved one never sent.
  it('sends a forgotten round\'s answers when a new one starts', async () => {
    const first = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => first.current.skipIntro())
    const saved = readSavedSession(UserId)!
    writeSavedSession({ ...saved, pending: [{ verb: 'z', promptForm: 'v1', known: false, responseMs: 1, mode: 'batch', group: 4 }] })
    apiMock.postVerbAnswers.mockClear()

    await arrange(batch([verb('a')]))

    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([expect.objectContaining({ verb: 'z' })])
    expect(readSavedSession(UserId)).toBeNull()
  })
})
