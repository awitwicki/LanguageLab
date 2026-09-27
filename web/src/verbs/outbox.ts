import type { VerbAnswerToPost } from '../api/client'

/** How long a failed chunk waits before its one retry. */
export const RetryDelayMs = 2000

/** What one request may carry, matching `IrregularVerbEndpoints.MaxAnswersPerRequest`. */
export const MaxPerRequest = 100

export type AnswerPoster = (answers: VerbAnswerToPost[]) => Promise<unknown>

export interface Outbox {
  /** Queues an answer and drains in the background. */
  add: (answer: VerbAnswerToPost) => void
  /** Resolves once everything queued has reached the server, and rejects if it could not. */
  flush: () => Promise<void>
  /** How many answers are still waiting. */
  pending: () => number
  /** The answers still waiting, in the order they will leave. */
  queued: () => VerbAnswerToPost[]
  /** Calls `listener` whenever an answer joins the queue or leaves it for the server. */
  subscribe: (listener: (queued: VerbAnswerToPost[]) => void) => () => void
}

function wait(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/**
 * Answers leave the browser from here, in the background, so a click is felt at once whatever
 * the ping. One request is in flight at a time and the chunks keep their order: the server
 * folds a verb's whole log in the order it landed in.
 *
 * A failed chunk is retried once and otherwise stays queued — a chunk leaves the queue only
 * after the server has taken it, so nothing is dropped and nothing is sent twice.
 *
 * `initial` holds answers left over from before a reload, sent ahead of anything added.
 */
export function createOutbox(post: AnswerPoster, initial: VerbAnswerToPost[] = []): Outbox {
  let queue: VerbAnswerToPost[] = [...initial]
  let draining: Promise<void> | null = null
  const listeners = new Set<(queued: VerbAnswerToPost[]) => void>()

  function changed() {
    listeners.forEach((listener) => listener(queue))
  }

  async function drain() {
    while (queue.length > 0) {
      const chunk = queue.slice(0, MaxPerRequest)

      try {
        await post(chunk)
      } catch {
        await wait(RetryDelayMs)
        // A second failure leaves the chunk queued and travels to flush()'s caller.
        await post(chunk)
      }

      queue = queue.slice(chunk.length)
      changed()
    }
  }

  function start(): Promise<void> {
    if (!draining) {
      // Deferred a microtask so a burst of synchronous add() calls all land in `queue` before
      // drain() takes its first slice — otherwise the first chunk would grab only whatever had
      // been pushed by the time this ran, not the whole batch.
      draining = Promise.resolve()
        .then(drain)
        .finally(() => {
          draining = null
        })
    }

    return draining
  }

  return {
    add(answer) {
      queue.push(answer)
      changed()
      // A background drain's failure is the next flush's to report, not an unhandled rejection.
      start().catch(() => {})
    },
    flush: () => (queue.length === 0 && !draining ? Promise.resolve() : start()),
    pending: () => queue.length,
    queued: () => [...queue],
    subscribe(listener) {
      listeners.add(listener)

      return () => listeners.delete(listener)
    },
  }
}
