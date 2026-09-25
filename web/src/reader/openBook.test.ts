import { describe, expect, it, vi } from 'vitest'
import { epub3Bytes } from '../test/epubFixtures'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { MemoryBookStore } from './bookStore'
import { sha256Hex } from './hash'
import { addBookToReader, openBookFile } from './openBook'

const apiMock = vi.hoisted(() => ({ registerReaderBook: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const file = (content: BlobPart, name = 'deaths-end.fb2') => new File([content], name)

describe('openBookFile', () => {
  it('keeps a readable book on the device under its hash', async () => {
    const store = new MemoryBookStore()
    const book = file(READER_BOOK_XML)

    const result = await openBookFile(book, store, null)

    const hash = await sha256Hex(await book.arrayBuffer())
    expect(result).toEqual({ kind: 'opened', hash })
    expect((await store.list())[0]).toMatchObject({ hash, title: "Death's End", author: 'Cixin Liu', fileName: 'deaths-end.fb2' })
  })

  it('stops at a different file than the one being continued, keeping nothing', async () => {
    const store = new MemoryBookStore()

    const result = await openBookFile(file(READER_BOOK_XML), store, 'f'.repeat(64))

    expect(result.kind).toBe('mismatch')
    expect(await store.list()).toEqual([])
  })

  it('names the problem with an unreadable file', async () => {
    const store = new MemoryBookStore()

    expect(await openBookFile(file('<not fb2'), store, null)).toEqual({
      kind: 'error',
      message: "This file isn't a readable fb2 or epub book.",
    })
  })

  it('keeps an epub on the device like any other book', async () => {
    const store = new MemoryBookStore()
    const book = file(new Uint8Array(epub3Bytes()), 'deaths-end.epub')

    expect((await openBookFile(book, store, null)).kind).toBe('opened')
    expect((await store.list())[0]).toMatchObject({ title: "Death's End", author: 'Cixin Liu' })
  })
})

describe('addBookToReader', () => {
  it('keeps the book on the device and registers it with the server', async () => {
    apiMock.registerReaderBook.mockReset().mockResolvedValue(null)
    const store = new MemoryBookStore()
    const bytes = await file(READER_BOOK_XML).arrayBuffer()
    const hash = await sha256Hex(bytes)

    await addBookToReader(store, bytes, 'deaths-end.fb2', hash)

    expect((await store.list())[0]).toMatchObject({ hash, title: "Death's End", fileName: 'deaths-end.fb2' })
    expect(apiMock.registerReaderBook).toHaveBeenCalledWith(hash, {
      title: "Death's End",
      author: 'Cixin Liu',
      chaptersCount: 2,
    })
  })
})
