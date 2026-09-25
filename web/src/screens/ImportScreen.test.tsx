import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { UploadProgress, UserRole } from '../api/client'
import type { WorkerResponse } from '../worker/parseBook.worker'
import { click, flush, render } from '../test/render'
import { MemoryBookStore } from '../reader/bookStore'
import { ImportScreen } from './ImportScreen'

const apiMock = vi.hoisted(() => ({
  importDictionary: vi.fn(),
  registerReaderBook: vi.fn(),
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
async function preview(bookStore?: MemoryBookStore, role: UserRole = 'user') {
  const onImported = vi.fn()
  const { container } = await render(<ImportScreen onImported={onImported} role={role} bookStore={bookStore} />)
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
  apiMock.registerReaderBook.mockReset().mockResolvedValue(null)
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

    const { container } = await render(<ImportScreen onImported={() => {}} role="user" />)

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

  it('offers a plain user publication as a request, not a switch', async () => {
    const { container } = await preview(undefined, 'user')

    expect(container.querySelector('.field.checkbox')?.textContent).toContain('Submit for review after import')
    expect(container.textContent).toContain('An administrator checks the dictionary before other users see it.')
  })

  it('offers an admin a plain visibility switch', async () => {
    const { container } = await preview(undefined, 'admin')

    expect(container.querySelector('.field.checkbox')?.textContent).toContain('Visible to all users')
    expect(container.textContent).not.toContain('An administrator checks the dictionary')
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
    let finish: (value: { dictionaryId: number; totalWords: number; newWords: number; reusedWords: number; droppedWords: number }) => void = () => {}

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

    await act(async () =>
      finish({
        dictionaryId: 42,
        totalWords: 1,
        newWords: 1,
        reusedWords: 0,
        droppedWords: 0,
      }),
    )
    await flush()

    // Click Continue button to navigate
    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    await click(continueBtn!)
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

  it('puts the imported book in the reader too', async () => {
    apiMock.importDictionary.mockResolvedValue({
      dictionaryId: 42,
      totalWords: 1,
      newWords: 1,
      reusedWords: 0,
      droppedWords: 0,
    })
    const store = new MemoryBookStore()
    const { container, worker, onImported } = await preview(store)

    await click(importButton(container))
    await worker.reply(aggregated)
    await flush()

    const [stored] = await store.list()
    expect(stored).toMatchObject({ title: 'Wool', fileName: 'wool.fb2' })
    expect(stored.hash).toMatch(/^[0-9a-f]{64}$/)
    expect(apiMock.registerReaderBook).toHaveBeenCalledWith(stored.hash, { title: 'Wool', author: '', chaptersCount: 2 })

    // Click Continue button to navigate
    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    await click(continueBtn!)
    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('still finishes the import when the reader could not take the book', async () => {
    apiMock.importDictionary.mockResolvedValue({
      dictionaryId: 42,
      totalWords: 1,
      newWords: 1,
      reusedWords: 0,
      droppedWords: 0,
    })
    apiMock.registerReaderBook.mockRejectedValue(new Error('offline'))
    const { container, worker, onImported } = await preview(new MemoryBookStore())

    await click(importButton(container))
    await worker.reply(aggregated)
    await flush()

    // Click Continue button to navigate
    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    await click(continueBtn!)
    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('shows the import result with dropped words and requires clicking Continue', async () => {
    apiMock.importDictionary.mockResolvedValue({
      dictionaryId: 42,
      totalWords: 10,
      newWords: 9,
      reusedWords: 1,
      droppedWords: 3,
    })
    const { container, worker, onImported } = await preview()

    await click(importButton(container))
    await worker.reply(aggregated)
    await flush()

    // Result summary should be visible with dropped words count
    expect(container.textContent).toContain('Dictionary created')
    expect(container.textContent).toContain('10')
    expect(container.textContent).toContain('Skipped')
    expect(container.textContent).toContain('3')
    expect(container.textContent).toContain('entries that are not English words')

    // onImported should NOT have been called yet
    expect(onImported).not.toHaveBeenCalled()

    // Click the Continue button
    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    expect(continueBtn?.textContent).toContain('Continue')
    await click(continueBtn!)

    // Now onImported should be called
    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('does not show the uploading progress once the result is showing', async () => {
    apiMock.importDictionary.mockResolvedValue({
      dictionaryId: 42,
      totalWords: 10,
      newWords: 9,
      reusedWords: 1,
      droppedWords: 0,
    })
    const { container, worker } = await preview()

    await click(importButton(container))
    await worker.reply(aggregated)
    await flush()

    expect(container.textContent).toContain('Dictionary created')
    expect(container.textContent).not.toContain('Saving on the server')
    expect(container.querySelector('.import-status')).toBeNull()
    // The now-pointless Import button and publication checkbox are gone too.
    expect(container.querySelector('.field.checkbox')).toBeNull()
    expect(container.querySelector('.btn-lg')).toBeNull()
  })
})
