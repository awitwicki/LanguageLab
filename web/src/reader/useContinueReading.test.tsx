import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderBookDto } from '../api/client'
import { flush, render } from '../test/render'
import { MemoryBookStore, type BookStore } from './bookStore'
import { useContinueReading } from './useContinueReading'

const apiMock = vi.hoisted(() => ({ listReaderBooks: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const hash = (n: number) => String(n).repeat(64)

/// The server hands the list back newest-first, which is what ReaderBookService orders by.
const serverBook = (n: number, title: string): ReaderBookDto => ({
  fileHash: hash(n),
  title,
  author: 'Hugh Howey',
  chaptersCount: 30,
  chapterIndex: 6,
  paragraphIndex: 0,
  sentenceIndex: 0,
  progress: 0.45,
  updatedAt: `2026-09-2${n}T10:00:00Z`,
  dictionaryId: null,
})

async function storeWith(...hashes: string[]): Promise<BookStore> {
  const store = new MemoryBookStore()

  for (const h of hashes) {
    await store.put({ hash: h, title: 't', author: '', fileName: 'f.fb2', addedAt: '2026-09-01T00:00:00Z' }, new ArrayBuffer(1))
  }

  return store
}

let latest: ReaderBookDto | null = null

function Probe({ store }: { store: BookStore | null }) {
  latest = useContinueReading(store)
  return null
}

// The return type is spelled out: the only assignment TypeScript can see here is `= null`,
// so without it the hook's result narrows to `never` for every caller.
async function mount(store: BookStore | null): Promise<ReaderBookDto | null> {
  latest = null
  await render(<Probe store={store} />)
  await flush()
  return latest
}

beforeEach(() => {
  apiMock.listReaderBooks.mockReset().mockResolvedValue([])
})

describe('useContinueReading', () => {
  it('offers the newest book whose file is on this device', async () => {
    apiMock.listReaderBooks.mockResolvedValue([serverBook(2, 'Shift'), serverBook(1, 'Wool')])

    expect((await mount(await storeWith(hash(1), hash(2))))?.title).toBe('Shift')
  })

  /// The file never leaves the browser that opened it, so a book read on the phone
  /// cannot be continued here — the next one down that is here wins instead.
  it('skips a book that is only on another device', async () => {
    apiMock.listReaderBooks.mockResolvedValue([serverBook(2, 'Shift'), serverBook(1, 'Wool')])

    expect((await mount(await storeWith(hash(1))))?.title).toBe('Wool')
  })

  it('offers nothing when no book in the library is on this device', async () => {
    apiMock.listReaderBooks.mockResolvedValue([serverBook(2, 'Shift')])

    expect(await mount(await storeWith(hash(1)))).toBeNull()
  })

  it('offers nothing when the library is empty', async () => {
    expect(await mount(await storeWith(hash(1)))).toBeNull()
  })

  /// The store opens asynchronously, so the first render has none.
  it('offers nothing until the store is open', async () => {
    apiMock.listReaderBooks.mockResolvedValue([serverBook(1, 'Wool')])

    expect(await mount(null)).toBeNull()
  })

  /// Best-effort, like the home screen's other requests: a failure leaves the row out.
  it('offers nothing when the library request fails', async () => {
    apiMock.listReaderBooks.mockRejectedValue(new Error('offline'))

    expect(await mount(await storeWith(hash(1)))).toBeNull()
  })

  it('offers nothing when the device store cannot be read', async () => {
    apiMock.listReaderBooks.mockResolvedValue([serverBook(1, 'Wool')])

    const broken = await storeWith(hash(1))
    broken.list = () => Promise.reject(new Error('blocked'))

    expect(await mount(broken)).toBeNull()
  })
})
