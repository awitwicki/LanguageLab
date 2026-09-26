import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderBookDto } from '../api/client'
import { click, flush, render } from '../test/render'
import { bytesOf, READER_BOOK_XML } from '../test/readerFixtures'
import { MemoryBookStore } from './bookStore'
import { sha256Hex } from './hash'
import { ReaderLibraryScreen } from './ReaderLibraryScreen'

const apiMock = vi.hoisted(() => ({
  listReaderBooks: vi.fn(),
  removeReaderBook: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const LOCAL = 'a'.repeat(64)
const ELSEWHERE = 'e'.repeat(64)

const dto = (fileHash: string, title: string, chapterIndex: number, progress: number): ReaderBookDto => ({
  fileHash,
  title,
  author: '',
  chaptersCount: 30,
  chapterIndex,
  paragraphIndex: 0,
  sentenceIndex: 0,
  progress,
  updatedAt: '2026-09-24T10:00:00.000Z',
  dictionaryId: null,
})

async function openLibrary(options: { persistent?: boolean; continueHash?: string } = {}) {
  const store = new MemoryBookStore()
  await store.put(
    { hash: LOCAL, title: 'Wool', author: 'Hugh Howey', fileName: 'wool.fb2', addedAt: '2026-09-20T10:00:00.000Z' },
    bytesOf('x'),
  )
  const onOpen = vi.fn()
  const view = await render(
    <ReaderLibraryScreen
      store={store}
      persistent={options.persistent ?? true}
      onOpen={onOpen}
      continueHash={options.continueHash ?? null}
    />,
  )
  await flush()
  return { ...view, store, onOpen }
}

async function choose(container: HTMLElement, content: string, name = 'deaths-end.fb2') {
  const input = container.querySelector<HTMLInputElement>('input[type="file"]')!
  Object.defineProperty(input, 'files', { value: [new File([content], name)], configurable: true })
  await act(async () => {
    input.dispatchEvent(new Event('change', { bubbles: true }))
  })
  await flush()
  await flush()
}

const button = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === text)

beforeEach(() => {
  apiMock.listReaderBooks.mockReset().mockResolvedValue([dto(LOCAL, 'Wool', 6, 0.23), dto(ELSEWHERE, 'Dune', 2, 0.05)])
  apiMock.removeReaderBook.mockReset().mockResolvedValue(null)
})

afterEach(() => vi.restoreAllMocks())

describe('ReaderLibraryScreen', () => {
  it('lists the books on this device with how far they are read', async () => {
    const { container, onOpen } = await openLibrary()
    const row = container.querySelector('[data-section="device"] .library-row')!

    expect(row.textContent).toContain('Wool')
    expect(row.textContent).toContain('Hugh Howey')
    expect(row.textContent).toContain('Chapter 7 of 30 · 23 %')

    await click(row.querySelector('.library-row-open')!)
    expect(onOpen).toHaveBeenCalledWith(LOCAL)
  })

  it('lists the books read elsewhere and asks for their file', async () => {
    const { container } = await openLibrary()
    const row = container.querySelector('[data-section="elsewhere"] .library-row')!

    expect(row.textContent).toContain('Dune')
    expect(button(row as HTMLElement, 'Open the file to continue')).toBeDefined()
  })

  it('opens a newly chosen book', async () => {
    const { container, onOpen } = await openLibrary()

    await click(button(container, 'Open a book')!)
    await choose(container, READER_BOOK_XML)

    expect(onOpen).toHaveBeenCalledWith(await sha256Hex(bytesOf(READER_BOOK_XML)))
  })

  it('says when the file for a book read elsewhere is a different one, and can open it as new', async () => {
    const { container, onOpen } = await openLibrary()

    await click(button(container, 'Open the file to continue')!)
    await choose(container, READER_BOOK_XML)

    expect(container.querySelector('.library-mismatch')!.textContent).toContain(
      'This is a different file than the one you read before',
    )
    expect(onOpen).not.toHaveBeenCalled()

    await click(button(container, 'Open as a new book')!)
    await flush()
    await flush()

    expect(onOpen).toHaveBeenCalledWith(await sha256Hex(bytesOf(READER_BOOK_XML)))
  })

  it('shows why a file cannot be opened', async () => {
    const { container } = await openLibrary()

    await click(button(container, 'Open a book')!)
    await choose(container, 'PK\u0003\u0004', 'book.fb2.zip')

    expect(container.querySelector('.library-error')!.textContent).toBe("This file isn't a readable fb2 or epub book.")
  })

  it('removes a book from this device only, or from the library too', async () => {
    const { container, store } = await openLibrary()

    await click(container.querySelector('[data-section="device"] [aria-label="More actions"]')!)
    await click(button(container, 'Remove from this device')!)
    await flush()

    expect(await store.list()).toEqual([])
    expect(apiMock.removeReaderBook).not.toHaveBeenCalled()

    const dune = [...container.querySelectorAll('[data-section="elsewhere"] .library-row')].find((row) =>
      row.textContent?.includes('Dune'),
    )!
    await click(dune.querySelector('[aria-label="More actions"]')!)
    await click(button(dune as HTMLElement, 'Remove from library')!)
    await flush()

    expect(apiMock.removeReaderBook).toHaveBeenCalledWith(ELSEWHERE)
  })

  it('closes an open row menu on Escape', async () => {
    const { container } = await openLibrary()

    await click(container.querySelector('[data-section="device"] [aria-label="More actions"]')!)
    expect(button(container, 'Remove from this device')).toBeDefined()

    await act(async () => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    })

    expect(button(container, 'Remove from this device')).toBeUndefined()
  })

  it('closes an open row menu on a press outside it', async () => {
    const { container } = await openLibrary()

    await click(container.querySelector('[data-section="device"] [aria-label="More actions"]')!)
    expect(button(container, 'Remove from this device')).toBeDefined()

    await act(async () => {
      document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
    })

    expect(button(container, 'Remove from this device')).toBeUndefined()
  })

  it('warns when books cannot be kept on this device', async () => {
    const { container } = await openLibrary({ persistent: false })

    expect(container.querySelector('.library-notice')!.textContent).toContain("can't keep books on this device")
  })

  /// The home screen's "Open the file" row lands here for a book it cannot open itself: the
  /// picker opens on arrival, already expecting that book's file.
  it('asks straight away for the file of the book it was sent to continue', async () => {
    const picker = vi.spyOn(HTMLInputElement.prototype, 'click')

    const { container, onOpen } = await openLibrary({ continueHash: ELSEWHERE })

    expect(picker).toHaveBeenCalledTimes(1)

    // A wrong file is caught as a mismatch, which only happens when the picker was opened
    // for that book rather than for a new one.
    await choose(container, READER_BOOK_XML)

    expect(container.querySelector('.library-mismatch')!.textContent).toContain('"Dune"')
    expect(onOpen).not.toHaveBeenCalled()
    expect(picker).toHaveBeenCalledTimes(1)
  })

  /// Only a book read elsewhere needs its file handed over; one that is already here is
  /// opened by the home screen itself.
  it('asks for nothing when the book it was sent is already on this device', async () => {
    const picker = vi.spyOn(HTMLInputElement.prototype, 'click')

    await openLibrary({ continueHash: LOCAL })

    expect(picker).not.toHaveBeenCalled()
  })

  it('asks for nothing when it was sent no book to continue', async () => {
    const picker = vi.spyOn(HTMLInputElement.prototype, 'click')

    await openLibrary()

    expect(picker).not.toHaveBeenCalled()
  })

  it('still lists local books when the server is unreachable', async () => {
    apiMock.listReaderBooks.mockRejectedValue(new Error('offline'))
    const { container } = await openLibrary()

    expect(container.querySelector('[data-section="device"]')!.textContent).toContain('Wool')
    expect(container.textContent).toContain("Couldn't load your library from the server")
  })
})
