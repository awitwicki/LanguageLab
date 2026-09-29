import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { UploadProgress, UserRole } from '../api/client'
import { click, flush, render } from '../test/render'
import { epub3Bytes } from '../test/epubFixtures'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { MemoryBookStore } from '../reader/bookStore'
import { ImportScreen } from './ImportScreen'

const apiMock = vi.hoisted(() => ({
  importDictionary: vi.fn(),
  registerReaderBook: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const book = `<?xml version="1.0" encoding="utf-8"?>
<FictionBook>
  <description><title-info><book-title>Wool</book-title></title-info></description>
  <body>
    <section><title><p>One</p></title><p>The silo was quiet.</p></section>
    <section><title><p>Two</p></title><p>Holston climbed.</p></section>
  </body>
</FictionBook>`

const RESULT = { dictionaryId: 42, totalWords: 1, newWords: 1, reusedWords: 0, droppedWords: 0, translationQueued: false }

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
async function preview(bookStore?: MemoryBookStore, role: UserRole = 'user', chosen = new File([book], 'wool.fb2')) {
  const onImported = vi.fn()
  const { container } = await render(<ImportScreen onImported={onImported} role={role} bookStore={bookStore} />)
  const input = container.querySelector<HTMLInputElement>('input[type="file"]')!

  Object.defineProperty(input, 'files', { value: [chosen] })

  await act(async () => {
    input.dispatchEvent(new Event('change', { bubbles: true }))
  })
  await flush()

  return { container, onImported }
}

beforeEach(() => {
  apiMock.importDictionary.mockReset().mockResolvedValue(RESULT)
  apiMock.registerReaderBook.mockReset().mockResolvedValue(null)
})

afterEach(() => vi.unstubAllGlobals())

describe('ImportScreen', () => {
  it('parses the chosen book into a chapter preview', async () => {
    const { container } = await preview()

    expect(container.querySelector('.field-value')?.textContent).toBe('Wool')
    expect(container.querySelectorAll('.chapter-preview li')).toHaveLength(2)
    expect(importButton(container).disabled).toBe(false)
  })

  it('parses a chosen epub into a chapter preview', async () => {
    const epub = new File([new Uint8Array(epub3Bytes())], 'deaths-end.epub')
    const { container } = await preview(undefined, 'user', epub)

    expect(container.querySelector('.field-value')?.textContent).toBe("Death's End")
    expect([...container.querySelectorAll('.chapter-preview li')].map((li) => li.textContent)).toEqual([
      'The Swordholder',
      'Year 62',
      'Chapter Three',
    ])
    expect(importButton(container).disabled).toBe(false)
  })

  it('offers no chapter level for a book with no nesting', async () => {
    const epub = new File([new Uint8Array(epub3Bytes())], 'deaths-end.epub')
    const { container } = await preview(undefined, 'user', epub)

    expect(container.querySelector('select')).toBeNull()
  })

  it('keeps the chapter level for a book whose sections nest', async () => {
    const { container } = await preview(undefined, 'user', new File([READER_BOOK_XML], 'deaths-end.fb2'))

    expect([...container.querySelectorAll('select option')].map((o) => o.textContent)).toEqual([
      'Leaf sections',
      'Level 1',
      'Level 2',
    ])
  })

  it('takes the file picker to both formats', async () => {
    const { container } = await render(<ImportScreen onImported={() => {}} role="user" />)

    expect(container.querySelector('input[type="file"]')?.getAttribute('accept')).toBe('.fb2,.epub,.zip')
    expect(container.querySelector('.dropzone')?.textContent).toContain('.epub')
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

  it('uploads the file itself, not a parsed word list', async () => {
    const { container } = await preview()

    await click(importButton(container))
    await flush()

    expect(apiMock.importDictionary).toHaveBeenCalledTimes(1)
    const [file, options] = apiMock.importDictionary.mock.calls[0]
    expect(file).toBeInstanceOf(File)
    expect(file.name).toBe('wool.fb2')
    expect(options).toEqual({ chapterMode: undefined, requestPublication: false })
  })

  it('sends the chosen chapter level, not leaf, once one is picked', async () => {
    const { container } = await preview(undefined, 'user', new File([READER_BOOK_XML], 'deaths-end.fb2'))

    await act(async () => {
      const select = container.querySelector('select')!
      select.value = '1'
      select.dispatchEvent(new Event('change', { bubbles: true }))
    })
    await click(importButton(container))
    await flush()

    const [, options] = apiMock.importDictionary.mock.calls[0]
    expect(options).toEqual({ chapterMode: 1, requestPublication: false })
  })

  it('shows the upload advancing, then the server saving, then hands over the new book', async () => {
    let report: UploadProgress | undefined
    let finish: (value: typeof RESULT) => void = () => {}

    apiMock.importDictionary.mockImplementation((_file: File, _options: unknown, onProgress: UploadProgress) => {
      report = onProgress
      return new Promise((resolve) => {
        finish = resolve
      })
    })

    const { container, onImported } = await preview()

    await click(importButton(container))
    expect(status(container)).toContain('Uploading')

    await act(async () => report?.(600, 1200))
    expect(status(container)).toContain('Uploading 1 kB')
    expect(status(container)).toContain('50%')
    expect(progressValue(container)).toBe('50')

    await act(async () => report?.(1200, 1200))
    expect(status(container)).toContain('Saving on the server')

    await act(async () => finish(RESULT))
    await flush()

    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    await click(continueBtn!)
    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('shows why the server refused the book and lets the admin try again', async () => {
    apiMock.importDictionary.mockRejectedValue(
      new Error('The server refused the upload as too large (HTTP 413).'),
    )

    const { container } = await preview()

    await click(importButton(container))
    await flush()

    expect(container.querySelector('.error')?.textContent).toContain('too large')
    expect(container.querySelector('.import-status')).toBeNull()
    expect(importButton(container).disabled).toBe(false)
  })

  it('puts the imported book in the reader too', async () => {
    const store = new MemoryBookStore()
    const { container, onImported } = await preview(store)

    await click(importButton(container))
    await flush()

    const [stored] = await store.list()
    expect(stored).toMatchObject({ title: 'Wool', fileName: 'wool.fb2' })
    expect(stored.hash).toMatch(/^[0-9a-f]{64}$/)
    expect(apiMock.registerReaderBook).toHaveBeenCalledWith(stored.hash, { title: 'Wool', author: '', chaptersCount: 2 })

    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    await click(continueBtn!)
    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('still finishes the import when the reader could not take the book', async () => {
    apiMock.registerReaderBook.mockRejectedValue(new Error('offline'))
    const { container, onImported } = await preview(new MemoryBookStore())

    await click(importButton(container))
    await flush()

    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    await click(continueBtn!)
    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('shows the import result with dropped words and requires clicking Continue', async () => {
    apiMock.importDictionary.mockResolvedValue({ ...RESULT, totalWords: 10, newWords: 9, reusedWords: 1, droppedWords: 3 })
    const { container, onImported } = await preview()

    await click(importButton(container))
    await flush()

    expect(container.textContent).toContain('Dictionary created')
    expect(container.textContent).toContain('10')
    expect(container.textContent).toContain('Skipped')
    expect(container.textContent).toContain('3')
    expect(container.textContent).toContain('entries that are not English words')

    expect(onImported).not.toHaveBeenCalled()

    const continueBtn = container.querySelector<HTMLButtonElement>('button:not(.btn-lg)')
    expect(continueBtn?.textContent).toContain('Continue')
    await click(continueBtn!)

    expect(onImported).toHaveBeenCalledWith(42)
  })

  it('does not show the uploading progress once the result is showing', async () => {
    const { container } = await preview()

    await click(importButton(container))
    await flush()

    expect(container.textContent).toContain('Dictionary created')
    expect(container.textContent).not.toContain('Saving on the server')
    expect(container.querySelector('.import-status')).toBeNull()
    expect(container.querySelector('.field.checkbox')).toBeNull()
    expect(container.querySelector('.btn-lg')).toBeNull()
  })

  it('offers the file picker inside Telegram too, since no worker is involved any more', async () => {
    vi.stubGlobal('Telegram', {
      WebApp: { initData: 'auth_date=1&hash=abc', ready: vi.fn(), expand: vi.fn(), openLink: vi.fn() },
    })

    const { container } = await render(<ImportScreen onImported={() => {}} role="user" />)

    expect(container.querySelector('input[type="file"]')).not.toBeNull()
    expect(container.querySelector('.import-notice')).toBeNull()
  })
})
