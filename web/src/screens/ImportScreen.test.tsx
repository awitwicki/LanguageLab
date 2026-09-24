import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { UploadProgress } from '../api/client'
import type { WorkerResponse } from '../worker/parseBook.worker'
import { click, flush, render } from '../test/render'
import { ImportScreen } from './ImportScreen'

const apiMock = vi.hoisted(() => ({
  importDictionary: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

/**
 * jsdom has no Worker. This stand-in records what the screen asks for and lets a test play
 * the worker's side: progress, the result, or a crash.
 */
class FakeWorker {
  static instances: FakeWorker[] = []

  onmessage: ((event: MessageEvent<WorkerResponse>) => void) | null = null
  onerror: ((event: ErrorEvent) => void) | null = null
  onmessageerror: ((event: MessageEvent) => void) | null = null
  requests: unknown[] = []

  constructor() {
    FakeWorker.instances.push(this)
  }

  postMessage(request: unknown) {
    this.requests.push(request)
  }

  terminate() {}

  reply(data: WorkerResponse) {
    return act(async () => {
      this.onmessage?.({ data } as MessageEvent<WorkerResponse>)
    })
  }

  crash(message: string) {
    return act(async () => {
      this.onerror?.({ message } as ErrorEvent)
    })
  }
}

const book = `<?xml version="1.0" encoding="utf-8"?>
<FictionBook>
  <description><title-info><book-title>Wool</book-title></title-info></description>
  <body>
    <section><title><p>One</p></title><p>The silo was quiet.</p></section>
    <section><title><p>Two</p></title><p>Holston climbed.</p></section>
  </body>
</FictionBook>`

const aggregated: WorkerResponse = {
  kind: 'aggregated',
  chapters: [{ order: 0, title: 'One', words: [{ word: 'silo', count: 1 }] }],
}

function importButton(container: HTMLElement) {
  return container.querySelector<HTMLButtonElement>('.btn-primary')!
}

function status(container: HTMLElement) {
  return container.querySelector('.import-status')?.textContent ?? ''
}

function progressValue(container: HTMLElement) {
  return container.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')
}

/** Renders the screen and takes it to the preview: a book chosen, "Import" ready to press. */
async function preview() {
  const onImported = vi.fn()
  const { container } = await render(<ImportScreen onImported={onImported} />)
  const input = container.querySelector<HTMLInputElement>('input[type="file"]')!

  Object.defineProperty(input, 'files', { value: [new File([book], 'wool.fb2')] })

  await act(async () => {
    input.dispatchEvent(new Event('change', { bubbles: true }))
  })
  await flush()

  return { container, onImported, worker: FakeWorker.instances[0] }
}

beforeEach(() => {
  FakeWorker.instances = []
  vi.stubGlobal('Worker', FakeWorker)
  apiMock.importDictionary.mockReset()
})

afterEach(() => vi.unstubAllGlobals())

describe('ImportScreen', () => {
  // The word extractor never starts inside Telegram's webview — the screen would hang at
  // "Starting the word extractor…" — so there the import is not offered at all.
  it('inside Telegram, says to import from a browser instead of offering the file picker', async () => {
    const openLink = vi.fn()
    vi.stubGlobal('Telegram', {
      WebApp: { initData: 'auth_date=1&hash=abc', ready: vi.fn(), expand: vi.fn(), openLink },
    })

    const { container } = await render(<ImportScreen onImported={() => {}} />)

    expect(container.querySelector('input[type="file"]')).toBeNull()

    const notice = container.querySelector('.import-notice')

    expect(notice?.textContent).toContain('browser')

    await click(notice!.querySelector('button')!)

    expect(openLink).toHaveBeenCalledWith(window.location.href)
  })

  it('parses the chosen book into a chapter preview', async () => {
    const { container } = await preview()

    expect(container.querySelector<HTMLInputElement>('.field input')?.value).toBe('Wool')
    expect(container.querySelectorAll('.chapter-preview li')).toHaveLength(2)
    expect(importButton(container).disabled).toBe(false)
  })

  it('shows how far the word extraction is, chapter by chapter', async () => {
    const { container, worker } = await preview()

    await click(importButton(container))

    expect(worker.requests).toHaveLength(1)
    expect(status(container)).toContain('Starting the word extractor')

    await worker.reply({ kind: 'progress', done: 0, total: 3 })
    expect(status(container)).toContain('chapter 0 of 3')

    await worker.reply({ kind: 'progress', done: 2, total: 3 })
    expect(status(container)).toContain('chapter 2 of 3')
    expect(progressValue(container)).toBe('67')
    // Progress is not the answer: the screen keeps waiting for the words.
    expect(importButton(container).disabled).toBe(true)
    expect(apiMock.importDictionary).not.toHaveBeenCalled()
  })

  it('surfaces a crashed or unloadable word extractor instead of waiting forever', async () => {
    const { container, worker } = await preview()

    await click(importButton(container))
    await worker.crash('Uncaught RangeError: out of memory')

    const error = container.querySelector('.error')?.textContent ?? ''

    expect(error).toContain('word extractor')
    expect(error).toContain('out of memory')
    expect(error).toContain('Reload')
    expect(container.querySelector('.import-status')).toBeNull()
    expect(importButton(container).disabled).toBe(false)
  })

  it('shows the upload advancing, then the server saving, then hands over the new book', async () => {
    let report: UploadProgress | undefined
    let finish: (value: { dictionaryId: number }) => void = () => {}

    apiMock.importDictionary.mockImplementation((_payload: unknown, onProgress: UploadProgress) => {
      report = onProgress
      return new Promise((resolve) => {
        finish = resolve
      })
    })

    const { container, worker, onImported } = await preview()

    await click(importButton(container))
    await worker.reply(aggregated)

    expect(apiMock.importDictionary).toHaveBeenCalledTimes(1)
    // The reader recognises the same file by this hash, see ReaderBook.FileHash.
    expect(apiMock.importDictionary.mock.calls[0][0].fileHash).toMatch(/^[0-9a-f]{64}$/)
    expect(status(container)).toContain('Uploading')

    await act(async () => report?.(600, 1200))
    expect(status(container)).toContain('Uploading 1 kB')
    expect(status(container)).toContain('50%')
    expect(progressValue(container)).toBe('50')

    await act(async () => report?.(1200, 1200))
    expect(status(container)).toContain('Saving on the server')

    await act(async () => finish({ dictionaryId: 42 }))
    await flush()

    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('shows why the server refused the book and lets the admin try again', async () => {
    apiMock.importDictionary.mockRejectedValue(
      new Error('The server refused the upload as too large (HTTP 413).'),
    )

    const { container, worker } = await preview()

    await click(importButton(container))
    await worker.reply(aggregated)
    await flush()

    expect(container.querySelector('.error')?.textContent).toContain('too large')
    expect(container.querySelector('.import-status')).toBeNull()
    expect(importButton(container).disabled).toBe(false)
  })
})
