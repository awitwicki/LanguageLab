import { describe, expect, it } from 'vitest'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { MemoryBookStore } from './bookStore'
import { sha256Hex } from './hash'
import { openBookFile } from './openBook'

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

    expect(await openBookFile(file(new Uint8Array([0x50, 0x4b, 3, 4]), 'book.fb2.zip'), store, null)).toEqual({
      kind: 'error',
      message: 'Unzip the book first.',
    })
    expect(await openBookFile(file('<not fb2'), store, null)).toEqual({
      kind: 'error',
      message: "This file isn't a readable fb2 book.",
    })
  })
})
