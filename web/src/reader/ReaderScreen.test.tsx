import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderBookDto } from '../api/client'
import { click, flush, render } from '../test/render'
import { bytesOf, READER_BOOK_XML } from '../test/readerFixtures'
import { MemoryBookStore } from './bookStore'
import { CHUNK_SENTENCES } from './chapterWindow'
import { ReaderScreen } from './ReaderScreen'
import { DEFAULT_SETTINGS } from './readerSettings'

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
  resetWord: vi.fn(),
  translateSentence: vi.fn(),
  importDictionary: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const lexiconMock = vi.hoisted(() => ({ loadLexicon: vi.fn() }))

vi.mock('../lexicon/lexicon', () => lexiconMock)

/** Every word READER_BOOK_XML and LONG_BOOK_XML actually use, mapped to itself or its lemma. */
const KNOWN_WORDS: Record<string, string> = {
  most: 'most', men: 'man', man: 'man', tried: 'try', try: 'try', to: 'to', adjust: 'adjust',
  silo: 'silo', quickly: 'quickly', holston: 'holston', climbed: 'climb', climb: 'climb',
  the: 'the', stairs: 'stair', stair: 'stair', paragraph: 'paragraph',
}

const FAKE_LEXICON = {
  lemmasOf: (form: string) => (KNOWN_WORDS[form] ? [KNOWN_WORDS[form]] : []),
  lemmaOf: (form: string) => KNOWN_WORDS[form] ?? null,
}

/**
 * jsdom has no IntersectionObserver: this one lets a test say what is on screen. The reader runs
 * two — one on the sentences, one on the gaps the unmounted chunks leave — so a test names an
 * element and this finds whichever observer is watching it.
 */
class FakeObserver {
  static instances: FakeObserver[] = []
  readonly callback: IntersectionObserverCallback
  targets: Element[] = []

  constructor(callback: IntersectionObserverCallback) {
    this.callback = callback
    FakeObserver.instances.push(this)
  }

  observe(target: Element) {
    this.targets.push(target)
  }

  disconnect() {
    this.targets = []
  }

  /** The sentence at that position is at the top of the screen. */
  static show(key: string) {
    FakeObserver.reveal(`[data-pos="${key}"]`)
  }

  /** The gap left by that chunk has come near the screen. */
  static showGap(chunk: number) {
    FakeObserver.reveal(`[data-chunk="${chunk}"]`)
  }

  private static reveal(selector: string) {
    for (const instance of FakeObserver.instances) {
      const target = instance.targets.find((element) => element.matches(selector))

      if (target) {
        instance.callback([{ target, isIntersecting: true } as unknown as IntersectionObserverEntry], instance as never)
        return
      }
    }

    throw new Error(`no observer is watching ${selector}`)
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

async function openReader(store = new MemoryBookStore()) {
  await store.put(
    { hash: HASH, title: "Death's End", author: 'Cixin Liu', fileName: 'deaths-end.fb2', addedAt: '2026-09-24T09:00:00.000Z' },
    bytesOf(READER_BOOK_XML),
  )
  const onBack = vi.fn()
  const view = await render(
    <ReaderScreen hash={HASH} store={store} onBack={onBack} onOpenDictionary={vi.fn()} />,
  )
  await flush()
  await flush()
  return { ...view, store, onBack }
}

/** One chapter of 120 one-sentence paragraphs: six chunks of CHUNK_SENTENCES, one chapter. */
const LONG_PARAGRAPHS = 120
const LONG_BOOK_XML = `<?xml version="1.0" encoding="utf-8"?>
<FictionBook><description><title-info><book-title>Wool</book-title></title-info></description><body><section>
  <title><p>Holston</p></title>
  ${Array.from({ length: LONG_PARAGRAPHS }, (_, index) => `<p>Paragraph ${index} climbed the stairs.</p>`).join('\n  ')}
</section></body></FictionBook>`

const LONG_HASH = 'b'.repeat(64)

/** The long book, opened where `at` says — the server's stored position. */
async function openLongReader(at: Partial<ReaderBookDto> = {}) {
  const store = new MemoryBookStore()
  await store.put(
    { hash: LONG_HASH, title: 'Wool', author: 'Hugh Howey', fileName: 'wool.fb2', addedAt: '2026-09-24T09:00:00.000Z' },
    bytesOf(LONG_BOOK_XML),
  )
  apiMock.listReaderBooks.mockResolvedValue([
    { ...serverBook, fileHash: LONG_HASH, title: 'Wool', chaptersCount: 1, chapterIndex: 0, paragraphIndex: 0, ...at },
  ])

  const view = await render(<ReaderScreen hash={LONG_HASH} store={store} onBack={vi.fn()} onOpenDictionary={vi.fn()} />)
  await flush()
  await flush()
  return view
}

const sentenceTexts = (container: HTMLElement) =>
  [...container.querySelectorAll('.reader-text')].map((p) => p.textContent)

beforeEach(() => {
  FakeObserver.instances = []
  vi.stubGlobal('IntersectionObserver', FakeObserver)
  Element.prototype.scrollIntoView = vi.fn()
  localStorage.clear()
  apiMock.readerCapabilities.mockReset().mockResolvedValue({ sentenceTranslation: true, importRetryAfterSeconds: null })
  apiMock.getWordStatuses.mockReset().mockResolvedValue({ learning: ['adjust'], known: [] })
  apiMock.listReaderBooks.mockReset().mockResolvedValue([serverBook])
  apiMock.registerReaderBook.mockReset().mockResolvedValue(serverBook)
  apiMock.saveReaderPosition.mockReset().mockResolvedValue(null)
  apiMock.getReaderWord.mockReset().mockResolvedValue({
    lemma: 'adjust',
    translation: 'налаштувати',
    source: 'dictionary',
    shelf: 'learning',
    canReset: true,
    learnTarget: 'personal',
  })
  apiMock.resetWord.mockReset().mockResolvedValue(null)
  apiMock.translateSentence.mockReset().mockResolvedValue({ status: 'ok', translation: 'Більшість чоловіків намагалися пристосуватися.' })
  lexiconMock.loadLexicon.mockReset().mockResolvedValue(FAKE_LEXICON)
  apiMock.importDictionary.mockReset().mockResolvedValue({
    dictionaryId: 77, totalWords: 0, newWords: 0, reusedWords: 0, droppedWords: 0, translationQueued: false,
  })
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

  it('highlights a word as new again once the panel undoes its shelf', async () => {
    const { container } = await openReader()
    const word = () => [...container.querySelectorAll('.reader-word')].find((w) => w.textContent === 'adjust')!

    await click(word())
    await flush()
    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Add to training')!)
    await flush()

    expect(apiMock.resetWord).toHaveBeenCalledWith('adjust')
    expect(word().className).toContain('reader-word-new')
    expect(word().className).not.toContain('reader-word-learning')
  })

  it('says the book is missing when its file is not on this device', async () => {
    const view = await render(
      <ReaderScreen hash={HASH} store={new MemoryBookStore()} onBack={vi.fn()} onOpenDictionary={vi.fn()} />,
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
    expect(await store.getTranslation(HASH, 'uk', '1.2.0')).toBe('Більшість чоловіків намагалися пристосуватися.')
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

    await act(async () => FakeObserver.show('1.1.0'))

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

      await act(async () => FakeObserver.show('1.2.0'))

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
        <ReaderScreen hash={HASH} store={store} onBack={vi.fn()} onOpenDictionary={vi.fn()} />,
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

  // Final review, Important 4: the lexicon-loading gate (wordStatus.ts's `lexicon: null` path)
  // had no ReaderScreen-level test — only the resolveWord unit test covered it.
  it('suppresses highlighting while the lexicon is still loading, then highlights once it resolves', async () => {
    let resolve: (lexicon: typeof FAKE_LEXICON) => void = () => {}
    lexiconMock.loadLexicon.mockReturnValue(new Promise((r) => (resolve = r)))

    const { container } = await openReader()

    const word = () => [...container.querySelectorAll('.reader-word')].find((w) => w.textContent === 'adjust')!
    expect(word().className).toBe('reader-word')

    await act(async () => resolve(FAKE_LEXICON))
    await flush()

    expect(word().className).toContain('reader-word-learning')
  })

  it('reads on without highlights when the lexicon fails to load', async () => {
    lexiconMock.loadLexicon.mockRejectedValue(new Error('offline'))
    const { container } = await openReader()

    expect(container.querySelector('.reader-notice')!.textContent).toBe('Word highlights unavailable')
    expect(sentenceTexts(container)).toContain('Most men tried to adjust.')
  })

  it('offers Build dictionary for a book with no dictionary yet', async () => {
    const { container } = await openReader()

    const button = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')
    expect(button).toBeDefined()
    expect(button!.disabled).toBe(false)
  })

  it('shows no button once the book already has a dictionary', async () => {
    apiMock.listReaderBooks.mockResolvedValue([{ ...serverBook, dictionaryId: 5 }])
    const { container } = await openReader()

    expect([...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')).toBeUndefined()
  })

  it('uploads the book and links the dictionary on success', async () => {
    apiMock.importDictionary.mockResolvedValue({
      dictionaryId: 77, totalWords: 1, newWords: 1, reusedWords: 0, droppedWords: 0, translationQueued: false,
    })
    const { container } = await openReader()

    apiMock.listReaderBooks.mockResolvedValue([{ ...serverBook, dictionaryId: 77 }])
    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')!)
    await flush()

    expect(apiMock.importDictionary).toHaveBeenCalledTimes(1)
    const [file, options] = apiMock.importDictionary.mock.calls[0]
    expect(file.name).toBe('deaths-end.fb2')
    expect(options).toEqual({ requestPublication: false })
    expect(apiMock.listReaderBooks).toHaveBeenCalledTimes(2)
    expect(apiMock.getWordStatuses).toHaveBeenCalledTimes(2)
    expect(container.querySelector('.reader-build-dictionary')).toBeNull()
  })

  // Review Focus: the button must vanish (not just relabel) while an upload is in flight, so a
  // second click cannot start a second concurrent upload.
  it('shows progress and hides the button while uploading', async () => {
    let report: ((sent: number, total: number) => void) | undefined

    apiMock.importDictionary.mockImplementation(
      (_file: File, _options: unknown, onProgress: (sent: number, total: number) => void) => {
        report = onProgress
        return new Promise(() => {})
      },
    )
    const { container } = await openReader()

    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')!)
    await act(async () => report?.(50, 100))

    expect(container.querySelector('.reader-build-dictionary')!.textContent).toContain('50 %')
    expect([...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')).toBeUndefined()
  })

  // The real message (api/client.ts's own error text), not a generic placeholder — matches
  // ImportScreen's error handling and covers the spec's 429-while-building case too.
  it('reads on and shows the real reason when building the dictionary fails', async () => {
    apiMock.importDictionary.mockRejectedValue(
      new Error('You already imported a book today. Try again in about 1 hour.'),
    )
    const { container } = await openReader()

    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')!)
    await flush()

    expect(container.querySelector('.reader-build-dictionary')!.textContent).toContain(
      'You already imported a book today. Try again in about 1 hour.',
    )
    expect(sentenceTexts(container)).toContain('Most men tried to adjust.')
  })

  it('disables the button and names the wait when the quota is spent', async () => {
    apiMock.readerCapabilities.mockResolvedValue({ sentenceTranslation: true, importRetryAfterSeconds: 3600 })
    const { container } = await openReader()

    const button = [...container.querySelectorAll('button')].find((b) => b.textContent?.startsWith('Build dictionary'))!
    expect(button.disabled).toBe(true)
    expect(button.textContent).toContain('about 1 hour')
  })

  it('offers Build dictionary inside Telegram too, since no worker is involved any more', async () => {
    vi.stubGlobal('Telegram', { WebApp: { initData: 'auth_date=1&hash=abc', ready: vi.fn(), expand: vi.fn(), openLink: vi.fn() } })
    const { container } = await openReader()

    expect([...container.querySelectorAll('button')].find((b) => b.textContent === 'Build dictionary')).toBeDefined()
  })

  it('puts only the chunks around the reading place in the DOM, gaps for the rest', async () => {
    const { container } = await openLongReader()

    // The chapter runs to 120 sentences; opening it mounts the chunk being read and one either
    // side (CHUNK_SENTENCES * 2 at the start of a chapter), and leaves the other four as gaps.
    expect(container.querySelectorAll('.reader-sentence')).toHaveLength(CHUNK_SENTENCES * 2)
    expect(container.querySelectorAll('.reader-gap')).toHaveLength(4)
    // Each gap says how many sentences it stands in for, which is what its height is built from.
    expect(container.querySelector('.reader-gap')!.getAttribute('style')).toContain(
      `--reader-gap-sentences: ${CHUNK_SENTENCES}`,
    )
    expect(sentenceTexts(container)).toContain('Paragraph 0 climbed the stairs.')
    expect(sentenceTexts(container)).not.toContain(`Paragraph ${LONG_PARAGRAPHS - 1} climbed the stairs.`)
  })

  it('mounts a chunk when its gap comes near the screen', async () => {
    const { container } = await openLongReader()

    await act(async () => FakeObserver.showGap(4))

    expect(sentenceTexts(container)).toContain(`Paragraph ${CHUNK_SENTENCES * 4} climbed the stairs.`)
    expect(container.querySelectorAll('.reader-gap')).toHaveLength(3)
    // Reading on from there: the sentences of a mounted chunk report the position like any other.
    await act(async () => FakeObserver.show(`0.${CHUNK_SENTENCES * 4}.0`))
    expect(JSON.parse(localStorage.getItem(`reader.position.${LONG_HASH}`)!).position).toEqual({
      chapterIndex: 0,
      paragraphIndex: CHUNK_SENTENCES * 4,
      sentenceIndex: 0,
    })
  })

  it('opens around a position deep in the chapter, not at its start', async () => {
    const paragraphIndex = CHUNK_SENTENCES * 3
    const { container } = await openLongReader({ paragraphIndex })

    expect(sentenceTexts(container)).toContain(`Paragraph ${paragraphIndex} climbed the stairs.`)
    // Three chunks around it; the chapter's own first sentence is a gap away.
    expect(container.querySelectorAll('.reader-sentence')).toHaveLength(CHUNK_SENTENCES * 3)
    expect(sentenceTexts(container)).not.toContain('Paragraph 0 climbed the stairs.')
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled()
  })

  it('renders the whole chapter where there is no IntersectionObserver', async () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    const { container } = await openLongReader()

    expect(container.querySelectorAll('.reader-sentence')).toHaveLength(LONG_PARAGRAPHS)
    expect(container.querySelector('.reader-gap')).toBeNull()
  })

  it('renders the whole chapter when the setting asks for it, gaps and all', async () => {
    localStorage.setItem('reader.settings', JSON.stringify({ ...DEFAULT_SETTINGS, wholeChapter: true }))
    const { container } = await openLongReader()

    // Every sentence is in the page — what find-in-page and a screen reader need — and no gap
    // stands in for anything, so nothing is left to mount later.
    expect(container.querySelectorAll('.reader-sentence')).toHaveLength(LONG_PARAGRAPHS)
    expect(container.querySelector('.reader-gap')).toBeNull()
    expect(sentenceTexts(container)).toContain(`Paragraph ${LONG_PARAGRAPHS - 1} climbed the stairs.`)
  })

  it('goes back to a window around the reading place when the whole chapter is turned off', async () => {
    localStorage.setItem('reader.settings', JSON.stringify({ ...DEFAULT_SETTINGS, wholeChapter: true }))
    const { container } = await openLongReader()

    // Read on, far from where the chapter was opened, then turn the setting off.
    const paragraphIndex = CHUNK_SENTENCES * 3
    await act(async () => FakeObserver.show(`0.${paragraphIndex}.0`))

    await click(container.querySelector('[aria-label="Contents and display"]')!)
    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Display')!)
    const group = container.querySelector('[aria-label="Whole chapter"]')!
    await click([...group.querySelectorAll('button')].find((b) => b.textContent === 'Off')!)

    // The window closes around the sentence being read, not around the chapter's opening position:
    // unmounting the reader's own surroundings would throw the page somewhere else.
    expect(sentenceTexts(container)).toContain(`Paragraph ${paragraphIndex} climbed the stairs.`)
    expect(container.querySelectorAll('.reader-sentence')).toHaveLength(CHUNK_SENTENCES * 3)
    expect(sentenceTexts(container)).not.toContain('Paragraph 0 climbed the stairs.')
  })
})
