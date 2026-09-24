import type { WorkerRequest, WorkerResponse } from '../worker/parseBook.worker'
import type { AggregatedChapter } from './aggregate'
import type { ChapterMode, SectionNode } from './chapters'

export type ExtractProgress = (done: number, total: number) => void

export interface WordExtractor {
  /** Lemmatizes the chapters in the worker. One request at a time; rejects with a user-facing message. */
  extract(sections: SectionNode[], mode: ChapterMode, onProgress?: ExtractProgress): Promise<AggregatedChapter[]>
  /** Stops the worker; a pending request rejects. */
  dispose(): void
}

interface Pending {
  resolve: (chapters: AggregatedChapter[]) => void
  reject: (error: Error) => void
  onProgress?: ExtractProgress
}

/**
 * The word-extraction worker behind a promise, shared by the import screen and the reader's
 * background import. A worker that never loads (a stale bundle after a deploy, a download that
 * broke off on mobile data) or dies mid-book (memory, on phones) never answers, so onerror and
 * onmessageerror reject rather than leave the caller waiting forever.
 */
export function createWordExtractor(): WordExtractor {
  const worker = new Worker(new URL('../worker/parseBook.worker.ts', import.meta.url), { type: 'module' })
  let pending: Pending | null = null

  const settle = (outcome: (request: Pending) => void) => {
    const request = pending
    pending = null

    if (request) outcome(request)
  }

  worker.onmessage = (event: MessageEvent<WorkerResponse>) => {
    const data = event.data

    if (data.kind === 'progress') {
      pending?.onProgress?.(data.done, data.total)
      return
    }

    if (data.kind === 'aggregated') {
      settle((request) => request.resolve(data.chapters))
      return
    }

    settle((request) => request.reject(new Error(data.message)))
  }

  worker.onerror = (event) => {
    const detail = event.message ? ` (${event.message})` : ''

    settle((request) => request.reject(new Error(`The word extractor failed${detail}. Reload the app and try again.`)))
  }

  worker.onmessageerror = () =>
    settle((request) =>
      request.reject(new Error('The word extractor sent an unreadable reply. Reload the app and try again.')),
    )

  return {
    extract(sections, mode, onProgress) {
      return new Promise((resolve, reject) => {
        pending = { resolve, reject, onProgress }
        const request: WorkerRequest = { kind: 'aggregate', sections, mode }
        worker.postMessage(request)
      })
    },
    dispose() {
      worker.terminate()
      settle((request) => request.reject(new Error('The word extractor was stopped.')))
    },
  }
}
