import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DrillQuery, SessionVerb, VerbExercise, VerbSession } from '../api/client'
import { RetryDelayMs } from './outbox'
import { readSavedSession, writeSavedSession } from './savedSession'
import { PassStreak } from './session'
import { useVerbSession } from './useVerbSession'

const apiMock = vi.hoisted(() => ({ getVerbSession: vi.fn(), postVerbAnswers: vi.fn(), setVerbsWordCount: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

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

type HookResult = { current: ReturnType<typeof useVerbSession> }

/// The card on screen's right option; a click with it advances at once.
const right = (result: HookResult) => result.current.card!.exercise.answer

/// Any option of the card on screen other than the right one.
const wrong = (result: HookResult) => result.current.card!.exercise.options.find((o) => o !== right(result))!

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.clear()
  apiMock.postVerbAnswers.mockResolvedValue({ results: [] })
  apiMock.setVerbsWordCount.mockResolvedValue(null)
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
    expect(result.current.words).toBe(3)
    expect(result.current.card).toBeNull()
  })

  it('reports a finished stage instead of a start screen', async () => {
    apiMock.getVerbSession.mockResolvedValue(null)
    const result = await renderHook(() => useVerbSession(BatchQuery))
    await flush()

    expect(result.current.phase).toBe('finished')
  })

  it('advances on a right pick without revealing anything', async () => {
    const result = await arrange()
    await act(async () => result.current.start())
    const answer = right(result)

    await act(async () => result.current.answer(answer))

    expect(result.current.revealed).toBe(false)
    expect(result.current.card!.verb.v1).not.toBe('a')
    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([
      expect.objectContaining({ verb: 'a', chosen: answer, mode: 'batch', group: 4 }),
    ])
  })

  it('holds the card on a wrong pick until Next', async () => {
    const result = await arrange()
    await act(async () => result.current.start())
    const wrongChoice = wrong(result)

    await act(async () => result.current.answer(wrongChoice))

    expect(result.current.revealed).toBe(true)
    expect(result.current.picked).toBe(wrongChoice)
    expect(result.current.card!.verb.v1).toBe('a')

    await act(async () => result.current.next())

    expect(result.current.revealed).toBe(false)
    expect(result.current.card!.verb.v1).not.toBe('a')
  })

  /// Review focus 4: a double tap, or a keypress landing on a card already answered.
  it('records one answer however often the verdict is pressed', async () => {
    const result = await arrange()
    await act(async () => result.current.start())
    const wrongChoice = wrong(result)
    const rightChoice = right(result)

    await act(async () => {
      result.current.answer(wrongChoice)
      result.current.answer(wrongChoice)
      result.current.answer(rightChoice)
    })

    expect(apiMock.postVerbAnswers).toHaveBeenCalledTimes(1)
    expect(apiMock.postVerbAnswers.mock.calls[0][0]).toHaveLength(1)
  })

  /// Review focus 4, the right-pick path: this instantly advances by calling advance() inside
  /// the same answer() call — a second verdict reaching the same still-stale card, in the same
  /// synchronous tick, must still be blocked, exactly as the wrong-pick path already is above.
  it('records one answer when a right pick is pressed twice in the same tick', async () => {
    const result = await arrange()
    await act(async () => result.current.start())
    const rightChoice = right(result)

    await act(async () => {
      result.current.answer(rightChoice)
      result.current.answer(rightChoice)
    })

    expect(apiMock.postVerbAnswers).toHaveBeenCalledTimes(1)
    expect(apiMock.postVerbAnswers.mock.calls[0][0]).toHaveLength(1)
  })

  it('counts a word that passes into the scope progress', async () => {
    const result = await arrange([verb('a')])
    await act(async () => result.current.start())

    expect(result.current.scope).toEqual({ passed: 2, total: 27 })

    for (let i = 0; i < PassStreak; i++) {
      await act(async () => result.current.answer(right(result)))
    }

    expect(result.current.phase).toBe('done')
    expect(result.current.scope).toEqual({ passed: 3, total: 27 })
  })

  /// Review focus 3: the next window is computed from what the server has recorded.
  it('drains the outbox before asking for the next words', async () => {
    const result = await arrange([verb('a')])
    await act(async () => result.current.start())

    for (let i = 0; i < PassStreak; i++) {
      await act(async () => result.current.answer(right(result)))
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

      await act(async () => result.current.start())

      for (let i = 0; i < PassStreak; i++) {
        await act(async () => result.current.answer(right(result)))
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

    await act(async () => result.current.answer(right(result)))

    expect(result.current.card!.verb.v1).toBe('b')
  })

  it('refills before the queue runs out', async () => {
    apiMock.getVerbSession.mockResolvedValue(free([verb('a')], 6))
    const result = await renderHook(() => useVerbSession(FreeQuery))
    await flush()

    expect(apiMock.getVerbSession).toHaveBeenCalledTimes(1)

    // Six cards and a refill at five left, so the first answer already crosses the line; the
    // chunk it fetches lands before the second one, which therefore asks for nothing.
    await act(async () => result.current.answer(right(result)))
    await act(async () => result.current.answer(right(result)))
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

    await act(async () => result.current.answer(right(result)))
    await act(async () => result.current.answer(right(result)))

    expect(result.current.card).toBeNull()
    expect(result.current.busy).toBe(true)
    expect(result.current.error).toBeNull()

    // A click with no card on screen must do nothing at all.
    await act(async () => result.current.answer('anything'))
    expect(apiMock.postVerbAnswers.mock.calls.flatMap((call) => call[0])).toHaveLength(2)

    await act(async () => {
      release(free([verb('b')], 20))
    })
    await flush()

    expect(result.current.card!.verb.v1).toBe('b')
    expect(result.current.busy).toBe(false)
  })

  it('walks a repeated form of one verb through its exercises', async () => {
    // queue: a/v1, a/v1 — the second showing takes the second exercise of v1.
    apiMock.getVerbSession.mockResolvedValue({
      mode: 'free',
      verbs: [verb('a')],
      queue: [
        { verb: 'a', promptForm: 'v1' },
        { verb: 'a', promptForm: 'v1' },
      ],
      scope: { passed: 0, total: 1 },
    })
    const result = await renderHook(() => useVerbSession(FreeQuery))
    await flush()

    expect(result.current.card!.exercise.before).toBe('S0 ')
    await act(async () => result.current.answer(right(result)))
    expect(result.current.card!.exercise.before).toBe('S1 ')
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
    await act(async () => result.current.start())
    await act(async () => result.current.answer(right(result)))
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
    await act(async () => result.current.start())
    await act(async () => result.current.answer(wrong(result)))

    expect(result.current.card!.verb.v1).toBe('a')
    expect(readSavedSession(UserId)!.drill!.current!.verb.v1).not.toBe('a')
  })

  it('resumes the drill round where it stopped without asking the server for words', async () => {
    const first = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => first.current.start())
    await act(async () => first.current.answer(right(first)))
    await act(async () => first.current.answer(wrong(first)))
    const next = readSavedSession(UserId)!.drill!.current!.verb.v1

    const result = await resume()

    expect(apiMock.getVerbSession).not.toHaveBeenCalled()
    expect(result.current.phase).toBe('drill')
    expect(result.current.revealed).toBe(false)
    expect(result.current.card!.verb.v1).toBe(next)
    expect(result.current.scope).toEqual({ passed: 2, total: 27 })
  })

  it('resumes a free run on its next card', async () => {
    const first = await arrange(free([verb('a'), verb('b'), verb('c')], 20), FreeQuery)
    await act(async () => first.current.answer(right(first)))
    await act(async () => first.current.answer(wrong(first)))

    const result = await resume(FreeQuery)

    expect(result.current.phase).toBe('drill')
    expect(result.current.card!.verb.v1).toBe('c')
  })

  it('sends the answers that had not left before the reload', async () => {
    apiMock.postVerbAnswers.mockRejectedValue(new Error('offline'))
    vi.useFakeTimers()

    let sentAnswer = ''

    try {
      apiMock.getVerbSession.mockResolvedValue(batch([verb('a'), verb('b'), verb('c')]))
      const first = await renderHook(() => useVerbSession(BatchQuery, { userId: UserId, title: Title }))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      await act(async () => first.current.start())
      sentAnswer = right(first)
      await act(async () => first.current.answer(sentAnswer))
      await act(async () => {
        await vi.advanceTimersByTimeAsync(RetryDelayMs * 2)
      })
    } finally {
      vi.useRealTimers()
    }

    expect(readSavedSession(UserId)!.pending).toHaveLength(1)

    apiMock.postVerbAnswers.mockResolvedValue({ results: [] })
    await resume()

    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([
      expect.objectContaining({ verb: 'a', chosen: sentAnswer }),
    ])
    expect(readSavedSession(UserId)!.pending).toEqual([])
  })

  it('forgets the round once it has passed and its answers are in', async () => {
    const result = await arrange(batch([verb('a')]))
    await act(async () => result.current.start())

    for (let i = 0; i < PassStreak; i++) {
      await act(async () => result.current.answer(right(result)))
    }

    await flush()

    expect(result.current.phase).toBe('done')
    expect(readSavedSession(UserId)).toBeNull()
  })

  it('forgets the round the learner walked away from', async () => {
    const result = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => result.current.start())
    await act(async () => result.current.answer(right(result)))

    await act(async () => result.current.discard())
    await flush()

    expect(readSavedSession(UserId)).toBeNull()
  })

  /// A fresh round started over a saved one takes over what the saved one never sent.
  it('sends a forgotten round\'s answers when a new one starts', async () => {
    const first = await arrange(batch([verb('a'), verb('b'), verb('c')]))
    await act(async () => first.current.start())
    const saved = readSavedSession(UserId)!
    writeSavedSession({
      ...saved,
      pending: [{ verb: 'z', promptForm: 'v1', chosen: 'zed', responseMs: 1, mode: 'batch', group: 4 }],
    })
    apiMock.postVerbAnswers.mockClear()

    await arrange(batch([verb('a')]))

    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([expect.objectContaining({ verb: 'z' })])
    expect(readSavedSession(UserId)).toBeNull()
  })
})
