import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { VerbAnswerToPost } from '../api/client'
import { createOutbox, MaxPerRequest, RetryDelayMs } from './outbox'

function answer(verb: string): VerbAnswerToPost {
  return { verb, promptForm: 'v1', known: true, responseMs: 500, mode: 'batch', group: 1 }
}

describe('the answer outbox', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('posts what it was given and empties out', async () => {
    const post = vi.fn().mockResolvedValue({ results: [] })
    const outbox = createOutbox(post)

    outbox.add(answer('a'))
    await outbox.flush()

    expect(post).toHaveBeenCalledWith([answer('a')])
    expect(outbox.pending()).toBe(0)
  })

  it('keeps one request in flight and the answers in order', async () => {
    let inFlight = 0
    const seen: string[][] = []
    const post = vi.fn(async (answers: VerbAnswerToPost[]) => {
      inFlight++
      expect(inFlight).toBe(1)
      seen.push(answers.map((a) => a.verb))
      await Promise.resolve()
      inFlight--
    })

    const outbox = createOutbox(post)
    outbox.add(answer('a'))
    outbox.add(answer('b'))
    outbox.add(answer('c'))
    await outbox.flush()

    expect(seen.flat()).toEqual(['a', 'b', 'c'])
  })

  it('retries a failed chunk once and then lets it go', async () => {
    const post = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValue({ results: [] })
    const outbox = createOutbox(post)

    outbox.add(answer('a'))
    const flushed = outbox.flush()
    await vi.advanceTimersByTimeAsync(RetryDelayMs)
    await flushed

    expect(post).toHaveBeenCalledTimes(2)
    expect(outbox.pending()).toBe(0)
  })

  it('keeps the answers and reports the failure when the retry fails too', async () => {
    const post = vi.fn().mockRejectedValue(new Error('offline'))
    const outbox = createOutbox(post)

    outbox.add(answer('a'))
    const flushed = outbox.flush()
    await vi.advanceTimersByTimeAsync(RetryDelayMs)

    await expect(flushed).rejects.toThrow('offline')
    expect(outbox.pending()).toBe(1)
  })

  it('tries again on the next flush after a failure', async () => {
    const post = vi
      .fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({ results: [] })
    const outbox = createOutbox(post)

    outbox.add(answer('a'))
    const failing = outbox.flush()
    await vi.advanceTimersByTimeAsync(RetryDelayMs)
    await expect(failing).rejects.toThrow()

    await outbox.flush()

    expect(outbox.pending()).toBe(0)
  })

  it('resolves at once when there is nothing to send', async () => {
    const post = vi.fn()

    await createOutbox(post).flush()

    expect(post).not.toHaveBeenCalled()
  })

  it('splits more answers than one request may carry', async () => {
    const post = vi.fn().mockResolvedValue({ results: [] })
    const outbox = createOutbox(post)

    for (let i = 0; i < MaxPerRequest + 3; i++) {
      outbox.add(answer(`v${i}`))
    }

    await outbox.flush()

    expect(post).toHaveBeenCalledTimes(2)
    expect(post.mock.calls[0][0]).toHaveLength(MaxPerRequest)
    expect(post.mock.calls[1][0]).toHaveLength(3)
  })
})
