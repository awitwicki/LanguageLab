import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DrillCard, DrillQuery, VerbAnswerResult } from '../api/client'
import { flush, render } from '../test/render'
import { useDrill } from './useDrill'

const apiMock = vi.hoisted(() => ({ nextVerbCard: vi.fn(), answerVerbCard: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function card(v1: string): DrillCard {
  return {
    verb: { v1, v2: `${v1}-2`, v3: `${v1}-3`, translation: `пер-${v1}`, group: 1, note: null },
    promptForm: 'v2',
    example: { tense: 'past', text: `Yesterday I [${v1}-2] it.` },
    scope: { passed: 0, total: 9 },
  }
}

const result: VerbAnswerResult = { verb: 'cut', mastery: 1, streak: 1, passed: false }

/// The hook under a probe component: the test reads what it returns. The query is hoisted
/// out of the component so it keeps one reference across renders, the same way a real
/// caller's route state does — a fresh literal every render would retrigger the effect on
/// every state update the effect itself causes.
function probe() {
  let latest: ReturnType<typeof useDrill> | null = null
  const query: DrillQuery = { mode: 'batch', group: 1 }

  function Probe() {
    latest = useDrill(query)
    return null
  }

  return { Probe, read: () => latest! }
}

describe('useDrill', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useRealTimers()
  })

  it('asks for a card on mount', async () => {
    apiMock.nextVerbCard.mockResolvedValue(card('cut'))
    const { Probe, read } = probe()

    await render(<Probe />)
    await flush()

    expect(apiMock.nextVerbCard).toHaveBeenCalledWith({ mode: 'batch', group: 1 })
    expect(read().card?.verb.v1).toBe('cut')
    expect(read().revealed).toBeNull()
  })

  it('reveals the answer, posts it, then prefetches the next card', async () => {
    apiMock.nextVerbCard.mockResolvedValueOnce(card('cut')).mockResolvedValueOnce(card('put'))
    apiMock.answerVerbCard.mockResolvedValue(result)
    const { Probe, read } = probe()

    await render(<Probe />)
    await flush()
    await act(async () => read().answer(true))
    await flush()

    expect(read().revealed).toEqual(result)
    expect(apiMock.answerVerbCard).toHaveBeenCalledWith(
      expect.objectContaining({ verb: 'cut', promptForm: 'v2', known: true, mode: 'batch', group: 1 }),
    )
    // The post has to land before the next card is picked, or the server picks on stale mastery.
    expect(apiMock.nextVerbCard).toHaveBeenLastCalledWith({ mode: 'batch', group: 1, exclude: 'cut' })

    await act(async () => read().next())
    expect(read().card?.verb.v1).toBe('put')
    expect(read().revealed).toBeNull()
  })

  it('reports how long the click took', async () => {
    apiMock.nextVerbCard.mockResolvedValue(card('cut'))
    apiMock.answerVerbCard.mockResolvedValue(result)
    const { Probe, read } = probe()

    // flush() resolves through a real setTimeout, and this hook's mount effect is
    // promise-driven — vi.useFakeTimers() would stall both, since nothing advances a fake
    // clock's macrotasks on its own. Pinning Date.now() directly avoids that entirely
    // while still controlling exactly what the hook measures as elapsed time.
    const now = vi.spyOn(Date, 'now').mockReturnValue(1_000_000)

    await render(<Probe />)
    await flush()

    now.mockReturnValue(1_002_400)
    await act(async () => read().answer(true))
    await flush()

    now.mockRestore()

    expect(apiMock.answerVerbCard).toHaveBeenCalledWith(expect.objectContaining({ responseMs: 2400 }))
  })

  /// Review focus 4: the server has no card identity, so the guard has to be here.
  it('posts once however often the button is clicked', async () => {
    apiMock.nextVerbCard.mockResolvedValue(card('cut'))
    apiMock.answerVerbCard.mockResolvedValue(result)
    const { Probe, read } = probe()

    await render(<Probe />)
    await flush()
    await act(async () => {
      read().answer(true)
      read().answer(true)
      read().answer(false)
    })
    await flush()

    expect(apiMock.answerVerbCard).toHaveBeenCalledTimes(1)
  })

  /// Review focus 4, found on review: next() with nothing prefetched (a failed prefetch, a
  /// finished stage, or Space pressed before the prefetch resolves) used to clear `revealed`
  /// and fetch a fresh card while leaving the just-answered `card` in place — reopening the
  /// verdict buttons on a card that was already posted. A second answer() during that gap
  /// posted twice and inflated the streak.
  it('does not double-post when next() runs with nothing prefetched', async () => {
    let resolveFetch: (card: DrillCard | null) => void = () => {}
    apiMock.nextVerbCard
      .mockResolvedValueOnce(card('cut')) // mount
      .mockResolvedValueOnce(null) // the prefetch after answering: nothing queued
      .mockReturnValueOnce(new Promise<DrillCard | null>((resolve) => { resolveFetch = resolve })) // next()'s own fetch
    apiMock.answerVerbCard.mockResolvedValue(result)
    const { Probe, read } = probe()

    await render(<Probe />)
    await flush()
    await act(async () => read().answer(true))
    await flush()

    expect(read().revealed).toEqual(result)

    // Two separate events, as a real Next click and a later keypress would be — not one
    // synchronous batch, which would hide the bug behind React's own update batching.
    await act(async () => read().next())

    // The revealed answer stays on screen while the fetch is pending — no flash back to
    // the blurred, unanswered state that would let a second verdict through.
    expect(read().revealed).toEqual(result)

    await act(async () => read().answer(true)) // a keypress arriving before the fetch resolves

    expect(apiMock.answerVerbCard).toHaveBeenCalledTimes(1)

    await act(async () => resolveFetch(card('put')))
    await flush()

    expect(read().card?.verb.v1).toBe('put')
    expect(read().revealed).toBeNull()
    // The card that was just shown is excluded from the fallback fetch too.
    expect(apiMock.nextVerbCard).toHaveBeenLastCalledWith({ mode: 'batch', group: 1, exclude: 'cut' })
  })

  it('ends the run when the stage has no card left', async () => {
    apiMock.nextVerbCard.mockResolvedValue(null)
    const { Probe, read } = probe()

    await render(<Probe />)
    await flush()

    expect(read().done).toBe(true)
    expect(read().card).toBeNull()
  })

  it('surfaces a failed post and lets the same card be answered again', async () => {
    apiMock.nextVerbCard.mockResolvedValue(card('cut'))
    apiMock.answerVerbCard.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(result)
    const { Probe, read } = probe()

    await render(<Probe />)
    await flush()
    await act(async () => read().answer(true))
    await flush()

    expect(read().error).toContain('offline')
    expect(read().revealed).toBeNull()

    await act(async () => read().answer(true))
    await flush()

    expect(read().revealed).toEqual(result)
    expect(read().error).toBeNull()
  })
})
