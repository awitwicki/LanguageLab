import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderBookDto } from '../api/client'
import { click, flush, render } from '../test/render'
import { bytesOf, READER_BOOK_XML } from '../test/readerFixtures'
import type { WorkerResponse } from '../worker/parseBook.worker'
import { MemoryBookStore } from './bookStore'
import { ReaderScreen } from './ReaderScreen'
import { resetAutoImportsForTests } from './useAutoImport'

const apiMock = vi.hoisted(() => ({
  readerCapabilities: vi.fn(),
  getWordStatuses: vi.fn(),
  listReaderBooks: vi.fn(),
  registerReaderBook: vi.fn(),
  saveReaderPosition: vi.fn(),
  getReaderWord: vi.fn(),
  learnWord: vi.fn(),
  knowWord: vi.fn(),
  ignoreWord: vi.fn(),
  translateSentence: vi.fn(),
  importDictionary: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

/** jsdom has no IntersectionObserver: this one lets a test say which sentences are on screen. */
class FakeObserver {
  static last: FakeObserver | null = null
  readonly callback: IntersectionObserverCallback
  readonly targets: Element[] = []

  constructor(callback: IntersectionObserverCallback) {
    this.callback = callback
    FakeObserver.last = this
  }

  observe(target: Element) {
    this.targets.push(target)
  }

  disconnect() {}

  show(key: string) {
    const target = this.targets.find((t) => (t as HTMLElement).dataset.pos === key)!
    this.callback([{ target, isIntersecting: true } as unknown as IntersectionObserverEntry], this as never)
  }
}

/** jsdom has no Worker: this one lets a test answer for the word extractor. */
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
}

const HASH = 'a'.repeat(64)

const serverBook: ReaderBookDto = {
  fileHash: HASH,
  title: "Death's End",
  author: 'Cixin Liu',
  chaptersCount: 2,
  chapterIndex: 1,
  paragraphIndex: 2,
  sentenceIndex: 0,
  progress: 0.8,
  updatedAt: '2026-09-24T10:00:00.000Z',
  dictionaryId: null,
}

async function openReader(store = new MemoryBookStore(), { canImport = false }: { canImport?: boolean } = {}) {
  await store.put(
    { hash: HASH, title: "Death's End", author: 'Cixin Liu', fileName: 'deaths-end.fb2', addedAt: '2026-09-24T09:00:00.000Z' },
    bytesOf(READER_BOOK_XML),
  )
  const onBack = vi.fn()
  const view = await render(
    <ReaderScreen hash={HASH} store={store} canImport={canImport} onBack={onBack} onOpenDictionary={vi.fn()} />,
  )
  await flush()
  await flush()
  return { ...view, store, onBack }
}

const sentenceTexts = (container: HTMLElement) =>
  [...container.querySelectorAll('.reader-text')].map((p) => p.textContent)

beforeEach(() => {
  vi.stubGlobal('IntersectionObserver', FakeObserver)
  Element.prototype.scrollIntoView = vi.fn()
  localStorage.clear()
  apiMock.readerCapabilities.mockReset().mockResolvedValue({ sentenceTranslation: true })
  apiMock.getWordStatuses.mockReset().mockResolvedValue({ learning: ['adjust'], known: [] })
  apiMock.listReaderBooks.mockReset().mockResolvedValue([serverBook])
  apiMock.registerReaderBook.mockReset().mockResolvedValue(serverBook)
  apiMock.saveReaderPosition.mockReset().mockResolvedValue(null)
  apiMock.getReaderWord.mockReset().mockResolvedValue({
    lemma: 'adjust',
    translation: 'налаштувати',
    source: 'dictionary',
    status: 'learning',
    learnTarget: 'personal',
  })
  apiMock.translateSentence.mockReset().mockResolvedValue({ status: 'ok', translation: 'Більшість чоловіків намагалися пристосуватися.' })
  FakeWorker.instances = []
  vi.stubGlobal('Worker', FakeWorker)
  resetAutoImportsForTests()
  apiMock.importDictionary.mockReset().mockResolvedValue({ dictionaryId: 77 })
})

afterEach(() => vi.unstubAllGlobals())

describe('ReaderScreen', () => {
  it("opens where the server's position is, with the chapter and book in the header", async () => {
    const { container } = await openReader()

    expect(container.querySelector('.reader-chapter-title')!.textContent).toBe('Year 62')
    expect(container.querySelector('.reader-book-title')!.textContent).toBe("Death's End")
    expect(sentenceTexts(container)).toContain('Most men tried to adjust.')
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled()
    // The saved sentence is scrolled to; there is no bookmark gutter any more.
    expect(container.querySelector('.reader-bookmark, .reader-gutter')).toBeNull()
  })

  it('highlights the words the learner is learning', async () => {
    const { container } = await openReader()

    const adjust = [...container.querySelectorAll('.reader-word')].find((w) => w.textContent === 'adjust')!
    expect(adjust.className).toContain('reader-word-learning')
  })

  it('says the book is missing when its file is not on this device', async () => {
    const view = await render(
      <ReaderScreen hash={HASH} store={new MemoryBookStore()} canImport={false} onBack={vi.fn()} onOpenDictionary={vi.fn()} />,
    )
    await flush()

    expect(view.container.textContent).toContain("This book isn't on this device")
  })

  it('registers a book the server does not know yet', async () => {
    apiMock.listReaderBooks.mockResolvedValue([])
    await openReader()

    expect(apiMock.registerReaderBook).toHaveBeenCalledWith(HASH, {
      title: "Death's End",
      author: 'Cixin Liu',
      chaptersCount: 2,
    })
  })

  it('goes to the previous chapter and back', async () => {
    const { container } = await openReader()

    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Previous chapter')!)
    expect(container.querySelector('.reader-chapter-title')!.textContent).toBe('The Swordholder')

    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Next chapter')!)
    expect(container.querySelector('.reader-chapter-title')!.textContent).toBe('Year 62')
  })

  it('opens the word panel on a tap and closes it on a tap beside the words', async () => {
    const { container } = await openReader()

    await click([...container.querySelectorAll('.reader-word')].find((w) => w.textContent === 'adjust')!)
    await flush()
    expect(apiMock.getReaderWord).toHaveBeenCalledWith('adjust', null)
    expect(container.querySelector('.word-panel')).not.toBeNull()

    await click(container.querySelector('.reader-body')!)
    expect(container.querySelector('.word-panel')).toBeNull()
  })

  it('translates a sentence and keeps the translation on the device', async () => {
    const { container, store } = await openReader()

    await click(container.querySelector('[data-pos="1.2.0"] .reader-strip')!)
    await flush()

    expect(container.querySelector('[data-pos="1.2.0"] .reader-translation')!.textContent).toBe(
      'Більшість чоловіків намагалися пристосуватися.',
    )
    expect(await store.getTranslation(HASH, '1.2.0')).toBe('Більшість чоловіків намагалися пристосуватися.')
  })

  it('names the daily limit when the server refuses', async () => {
    apiMock.translateSentence.mockResolvedValue({ status: 'limit' })
    const { container } = await openReader()

    await click(container.querySelector('[data-pos="1.2.0"] .reader-strip')!)
    await flush()

    expect(container.querySelector('.reader-translation-error')!.textContent).toContain(
      'Daily sentence translation limit reached',
    )
  })

  it('hides the translate strip without sentence translation', async () => {
    apiMock.readerCapabilities.mockResolvedValue({ sentenceTranslation: false })
    const { container } = await openReader()

    expect(container.querySelector('.reader-strip')).toBeNull()
  })

  it('remembers the sentence at the top of the screen', async () => {
    await openReader()

    await act(async () => FakeObserver.last!.show('1.1.0'))

    expect(JSON.parse(localStorage.getItem(`reader.position.${HASH}`)!).position).toEqual({
      chapterIndex: 1,
      paragraphIndex: 1,
      sentenceIndex: 0,
    })
  })

  // FakeObserver.show() reports exactly the key it's given — it does not simulate real
  // IntersectionObserver geometry, so it cannot by itself reproduce the drift bug (two
  // independent hardcoded guesses at the header height disagreeing). What it CAN show is that
  // the two numbers are no longer independent guesses: both the scroll offset (Sentence.css's
  // scroll-margin-top) and the reading band's rootMargin are now driven by one measured value,
  // exposed here as the --reader-header-height custom property. We assert (a) that property
  // carries the header's real measured height, not a hardcoded constant, and (b) a sentence
  // reported by the observer is still saved as itself — no regression in the unrelated,
  // already-covered position-tracking path.
  it('drives the scroll offset and reading band from one measured header height', async () => {
    const rect = vi
      .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
      .mockReturnValue({ height: 90, width: 0, top: 0, left: 0, right: 0, bottom: 90, x: 0, y: 0, toJSON: () => undefined } as DOMRect)

    try {
      const { container } = await openReader()

      expect(container.querySelector('.reader')!.getAttribute('style')).toContain('--reader-header-height: 90px')

      await act(async () => FakeObserver.last!.show('1.2.0'))

      expect(JSON.parse(localStorage.getItem(`reader.position.${HASH}`)!).position).toEqual({
        chapterIndex: 1,
        paragraphIndex: 2,
        sentenceIndex: 0,
      })
    } finally {
      rect.mockRestore()
    }
  })

  it("opens with the device's position when the server's book list never answers", async () => {
    vi.useFakeTimers()

    try {
      apiMock.listReaderBooks.mockReturnValue(new Promise(() => {}))

      const store = new MemoryBookStore()
      await store.put(
        { hash: HASH, title: "Death's End", author: 'Cixin Liu', fileName: 'deaths-end.fb2', addedAt: '2026-09-24T09:00:00.000Z' },
        bytesOf(READER_BOOK_XML),
      )

      const { container } = await render(
        <ReaderScreen hash={HASH} store={store} canImport={false} onBack={vi.fn()} onOpenDictionary={vi.fn()} />,
      )

      // The device-side effects (reading the file from IndexedDB, word statuses) run on real
      // microtasks, not timers — give them a chance to settle while the fake clock stays at 0.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })

      expect(container.querySelector('.reader-chapter-title')).toBeNull()
      expect(container.textContent).toContain('Opening the book')

      // Past the 3s timeout, the server is treated as unreachable (not still pending) and the
      // reader opens anyway, with whatever position the device already has.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(3000)
      })

      expect(container.querySelector('.reader-chapter-title')).not.toBeNull()
    } finally {
      vi.useRealTimers()
    }
  })

  it('reads on without highlights when the statuses fail', async () => {
    apiMock.getWordStatuses.mockRejectedValue(new Error('offline'))
    const { container } = await openReader()

    expect(container.querySelector('.reader-notice')!.textContent).toBe('Word highlights unavailable')
    expect(sentenceTexts(container)).toContain('Most men tried to adjust.')
  })

  it("builds the book's dictionary in the background for an uploader, then links it", async () => {
    const { container } = await openReader(new MemoryBookStore(), { canImport: true })
    const worker = FakeWorker.instances[0]

    expect(worker.requests[0]).toMatchObject({ kind: 'aggregate', mode: 'leaf' })

    await worker.reply({ kind: 'progress', done: 1, total: 2 })
    expect(container.querySelector('.reader-notice')!.textContent).toBe("Building this book's word list… 50 %")

    apiMock.listReaderBooks.mockResolvedValue([{ ...serverBook, dictionaryId: 77 }])
    await worker.reply({ kind: 'aggregated', chapters: [{ order: 0, title: 'One', words: [{ word: 'silo', count: 2 }] }] })
    await flush()

    expect(apiMock.importDictionary).toHaveBeenCalledWith({
      name: "Death's End",
      requestPublication: false,
      fileHash: HASH,
      chapters: [{ order: 0, title: 'One', words: [{ word: 'silo', count: 2 }] }],
    })
    expect(apiMock.listReaderBooks).toHaveBeenCalledTimes(2)
    expect(apiMock.getWordStatuses).toHaveBeenCalledTimes(2)
    expect(container.querySelector('.reader-notice')).toBeNull()
  })

  it('reads on and says so when building the word list fails', async () => {
    apiMock.importDictionary.mockRejectedValue(new Error('500'))
    const { container } = await openReader(new MemoryBookStore(), { canImport: true })

    await FakeWorker.instances[0].reply({ kind: 'aggregated', chapters: [] })
    await flush()

    expect(container.querySelector('.reader-notice')!.textContent).toBe("Couldn't build this book's word list")
    expect(sentenceTexts(container)).toContain('Most men tried to adjust.')
  })

  it('imports nothing for an ordinary user', async () => {
    await openReader()

    expect(FakeWorker.instances).toHaveLength(0)
    expect(apiMock.importDictionary).not.toHaveBeenCalled()
  })

  it('imports nothing when the book already has a dictionary', async () => {
    apiMock.listReaderBooks.mockResolvedValue([{ ...serverBook, dictionaryId: 5 }])
    await openReader(new MemoryBookStore(), { canImport: true })

    expect(FakeWorker.instances).toHaveLength(0)
  })

  it('imports nothing inside Telegram, where the worker does not start', async () => {
    vi.stubGlobal('Telegram', { WebApp: { initData: 'auth_date=1&hash=abc', ready: vi.fn(), expand: vi.fn(), openLink: vi.fn() } })
    await openReader(new MemoryBookStore(), { canImport: true })

    expect(FakeWorker.instances).toHaveLength(0)
  })

  it('tries again next time when the reader was left mid-import', async () => {
    const store = new MemoryBookStore()
    const first = await openReader(store, { canImport: true })
    expect(FakeWorker.instances).toHaveLength(1)

    await first.unmount()
    await openReader(store, { canImport: true })

    expect(FakeWorker.instances).toHaveLength(2)
  })
})
