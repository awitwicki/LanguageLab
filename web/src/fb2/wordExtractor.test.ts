import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WorkerResponse } from '../worker/parseBook.worker'
import { createWordExtractor } from './wordExtractor'

/** jsdom has no Worker: this one records requests and lets a test answer for the worker. */
class FakeWorker {
  static instances: FakeWorker[] = []
  onmessage: ((event: MessageEvent<WorkerResponse>) => void) | null = null
  onerror: ((event: ErrorEvent) => void) | null = null
  onmessageerror: ((event: MessageEvent) => void) | null = null
  requests: unknown[] = []
  terminated = false

  constructor() {
    FakeWorker.instances.push(this)
  }

  postMessage(request: unknown) {
    this.requests.push(request)
  }

  terminate() {
    this.terminated = true
  }

  reply(data: WorkerResponse) {
    this.onmessage?.({ data } as MessageEvent<WorkerResponse>)
  }
}

const chapters = [{ order: 0, title: 'One', words: [{ word: 'silo', count: 2 }] }]

beforeEach(() => {
  FakeWorker.instances = []
  vi.stubGlobal('Worker', FakeWorker)
})

afterEach(() => vi.unstubAllGlobals())

describe('createWordExtractor', () => {
  it('asks the worker for the chapters and reports its progress', async () => {
    const extractor = createWordExtractor()
    const progress: [number, number][] = []

    const result = extractor.extract([], 'leaf', (done, total) => progress.push([done, total]))
    const worker = FakeWorker.instances[0]
    worker.reply({ kind: 'progress', done: 1, total: 2 })
    worker.reply({ kind: 'aggregated', chapters })

    expect(await result).toEqual(chapters)
    expect(worker.requests).toEqual([{ kind: 'aggregate', sections: [], mode: 'leaf' }])
    expect(progress).toEqual([[1, 2]])
  })

  it("rejects with the worker's own error message", async () => {
    const extractor = createWordExtractor()
    const result = extractor.extract([], 'leaf')

    FakeWorker.instances[0].reply({ kind: 'error', message: 'Out of memory.' })

    await expect(result).rejects.toThrow('Out of memory.')
  })

  it('rejects when the worker crashes instead of waiting forever', async () => {
    const extractor = createWordExtractor()
    const result = extractor.extract([], 'leaf')

    FakeWorker.instances[0].onerror?.({ message: 'boom' } as ErrorEvent)

    await expect(result).rejects.toThrow('The word extractor failed (boom). Reload the app and try again.')
  })

  it('stops the worker and the pending request on dispose', async () => {
    const extractor = createWordExtractor()
    const result = extractor.extract([], 'leaf')

    extractor.dispose()

    await expect(result).rejects.toThrow('The word extractor was stopped.')
    expect(FakeWorker.instances[0].terminated).toBe(true)
  })
})
