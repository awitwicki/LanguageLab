import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type MouseEvent,
} from 'react'
import { api, type ReaderBookDto } from '../api/client'
import { telegramInitData } from '../auth/telegram'
import { formatInt } from '../lib/format'
import type { BookStore } from './bookStore'
import { allChunks, chunkChapter, chunkOfPosition, chunksAround } from './chapterWindow'
import {
  chapterProgress,
  clampPosition,
  comparePositions,
  parsePositionKey,
  positionKey,
  readBookFile,
  type ReaderBook,
  type ReaderPosition,
} from './readerBook'
import { ReaderMenu } from './ReaderMenu'
import { loadSettings, resolveTheme, saveSettings, TEXT_SIZES, type ReaderSettings } from './readerSettings'
import { Sentence } from './Sentence'
import { useAutoImport } from './useAutoImport'
import { loadLocalPosition, pickPosition, useReaderPosition } from './useReaderPosition'
import { useSentenceTranslations } from './useSentenceTranslations'
import { WordPanel } from './WordPanel'
import { bookLemmaCounts, EMPTY_STATUSES, resolveWord, toStatusMap, type KnownStatuses, type ResolvedWord } from './wordStatus'
import './ReaderScreen.css'

interface Props {
  hash: string
  store: BookStore
  onBack: () => void
  onOpenDictionary: (dictionaryId: number) => void
}

type Load =
  | { status: 'loading' }
  | { status: 'missing' }
  | { status: 'error'; message: string }
  | { status: 'ready'; book: ReaderBook; bytes: ArrayBuffer }

interface Selection {
  key: string
  tokenIndex: number
  lemma: string
  form: string
  count: number
}

const START: ReaderPosition = { chapterIndex: 0, paragraphIndex: 0, sentenceIndex: 0 }

/** Fallback before the header's real height is measured — matches Sentence.css's old default. */
const DEFAULT_HEADER_HEIGHT = 72

/**
 * How far off the screen a gap mounts the chunk it stands for — about a screen ahead of the
 * reading direction, so the sentences are there before they are scrolled to, and a little behind.
 */
const GAP_MARGIN = '800px 0px 1200px 0px'

const NO_CHUNKS: ReadonlySet<number> = new Set()

function usePrefersDark(): boolean {
  const [dark, setDark] = useState(() => window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false)

  useEffect(() => {
    const query = window.matchMedia?.('(prefers-color-scheme: dark)')
    if (!query) return

    const onChange = (event: MediaQueryListEvent) => setDark(event.matches)
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  return dark
}

export function ReaderScreen({ hash, store, onBack, onOpenDictionary }: Props) {
  const [load, setLoad] = useState<Load>({ status: 'loading' })
  /** undefined until the server answered; null when it does not know the book (or failed). */
  const [server, setServer] = useState<ReaderBookDto | null | undefined>(undefined)
  const [canTranslate, setCanTranslate] = useState(false)
  const [statuses, setStatuses] = useState<KnownStatuses>(EMPTY_STATUSES)
  const [statusesFailed, setStatusesFailed] = useState(false)
  const [position, setPosition] = useState<ReaderPosition | null>(null)
  const [selection, setSelection] = useState<Selection | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const [settings, setSettings] = useState<ReaderSettings>(loadSettings)
  const prefersDark = usePrefersDark()
  const bodyRef = useRef<HTMLElement>(null)
  const headerRef = useRef<HTMLElement>(null)
  const [headerHeight, setHeaderHeight] = useState(DEFAULT_HEADER_HEIGHT)
  /** Where the reader jumps to and opens around: the book's opening place, then every jump. */
  const [target, setTarget] = useState<{ id: number; position: ReaderPosition } | null>(null)
  /** The chunks of the chapter that are in the DOM, and the target they were opened around. */
  const [chunkWindow, setChunkWindow] = useState<{ id: number; mounted: ReadonlySet<number> } | null>(null)
  const scrolledTo = useRef(-1)
  const anchorShift = useRef<{ key: string; top: number } | null>(null)
  const positionRef = useRef<ReaderPosition | null>(null)

  const book = load.status === 'ready' ? load.book : null
  const report = useReaderPosition(hash, book)
  const { translations, toggle } = useSentenceTranslations(hash, store)
  const counts = useMemo(() => (book ? bookLemmaCounts(book) : new Map<string, number>()), [book])

  const bytes = load.status === 'ready' ? load.bytes : null

  // After the dictionary is built: link it (dictionaryId) and pick up its words' statuses.
  const refreshAfterImport = useCallback(() => {
    api
      .listReaderBooks()
      .then((books) => setServer(books.find((b) => b.fileHash === hash) ?? null))
      .catch(() => undefined)
    api
      .getWordStatuses()
      .then((found) => setStatuses(toStatusMap(found)))
      .catch(() => undefined)
  }, [hash])

  const autoImport = useAutoImport({
    // Only a registered book whose dictionaryId is known to be null — not a server that timed out.
    enabled: telegramInitData() === null && server != null && server.dictionaryId === null,
    hash,
    title: book?.title ?? '',
    bytes,
    onImported: refreshAfterImport,
  })

  // The file, from this device.
  useEffect(() => {
    let cancelled = false

    Promise.all([store.getFile(hash), store.list()])
      .then(([bytes, metas]) => {
        if (cancelled) return
        if (!bytes) {
          setLoad({ status: 'missing' })
          return
        }

        const fileName = metas.find((meta) => meta.hash === hash)?.fileName ?? 'book'

        try {
          setLoad({ status: 'ready', book: readBookFile(bytes, fileName), bytes })
        } catch (e) {
          setLoad({ status: 'error', message: e instanceof Error ? e.message : String(e) })
        }
      })
      .catch((e) => {
        if (!cancelled) setLoad({ status: 'error', message: e instanceof Error ? e.message : String(e) })
      })

    return () => {
      cancelled = true
    }
  }, [hash, store])

  // The header's real rendered height drives both the scroll offset (Sentence.css) and the
  // reading-band's rootMargin below, so the two can never disagree about where the header ends.
  // env(safe-area-inset-top) makes it vary by device, and orientation change can alter that inset.
  useLayoutEffect(() => {
    const measure = () => {
      const height = headerRef.current?.getBoundingClientRect().height
      if (height) setHeaderHeight(height)
    }

    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  })

  // What the server knows; each part may fail on its own without stopping the reading.
  useEffect(() => {
    let cancelled = false

    api
      .readerCapabilities()
      .then((capabilities) => !cancelled && setCanTranslate(capabilities.sentenceTranslation))
      .catch(() => undefined)

    api
      .getWordStatuses()
      .then((found) => !cancelled && setStatuses(toStatusMap(found)))
      .catch(() => !cancelled && setStatusesFailed(true))

    // A stalled (not merely offline — offline fails fast) connection must not leave the reader
    // stuck on "Opening the book…" forever: the file and any local position are already on the
    // device, so after 3s with no answer yet, treat the server as unreachable. A late real
    // response still wins if server is still undefined when it arrives.
    const serverTimeout = window.setTimeout(() => {
      if (!cancelled) setServer((current) => (current === undefined ? null : current))
    }, 3000)

    api
      .listReaderBooks()
      .then((books) => !cancelled && setServer(books.find((b) => b.fileHash === hash) ?? null))
      .catch(() => !cancelled && setServer(null))

    return () => {
      cancelled = true
      window.clearTimeout(serverTimeout)
    }
  }, [hash])

  // A book the server does not know (first open, or a registration that failed before).
  useEffect(() => {
    if (!book || server !== null) return

    api
      .registerReaderBook(hash, { title: book.title, author: book.author, chaptersCount: book.chapters.length })
      .then(setServer)
      .catch(() => undefined)
  }, [book, server, hash])

  // Where to open: the later of this device and the server.
  useEffect(() => {
    if (!book || server === undefined || position !== null) return

    const start = clampPosition(book, pickPosition(loadLocalPosition(hash), server) ?? START)
    setTarget({ id: 0, position: start })
    setPosition(start)
  }, [book, server, position, hash])

  const chapterIndex = position?.chapterIndex ?? null
  const chapter = book && chapterIndex !== null ? book.chapters[chapterIndex] : null
  const chunks = useMemo(
    () => (chapter && chapterIndex !== null ? chunkChapter(chapter, chapterIndex) : []),
    [chapter, chapterIndex],
  )

  // A chapter is not put in the DOM whole: the chunks around the place being read are, and the
  // rest stand as gaps until one comes near the screen (the observer below). A long chapter — a
  // book with no structure of its own is one — would otherwise cost a phone every sentence of it
  // at once. Without IntersectionObserver there is nothing to grow the window, so it is the
  // whole chapter, as it was before.
  const startChunks = useMemo<ReadonlySet<number>>(
    () =>
      chapter && target
        ? new Set(
            typeof IntersectionObserver === 'undefined'
              ? allChunks(chunks.length)
              : chunksAround(chunkOfPosition(chapter, target.position), chunks.length),
          )
        : NO_CHUNKS,
    [chapter, chunks.length, target],
  )

  const mountedChunks = chunkWindow && target && chunkWindow.id === target.id ? chunkWindow.mounted : startChunks

  // A jump has to be mounted around in the same commit that shows it, so the window follows the
  // target here in the render rather than in an effect.
  if (chapter && target && chunkWindow?.id !== target.id) {
    setChunkWindow({ id: target.id, mounted: startChunks })
  }

  useEffect(() => {
    positionRef.current = position
  }, [position])

  // The jump itself: as soon as the target's own chunk is in the DOM, scroll to it — once.
  useLayoutEffect(() => {
    if (!target || scrolledTo.current === target.id) return

    const element = bodyRef.current?.querySelector(`[data-pos="${positionKey(target.position)}"]`)
    if (!element) return

    scrolledTo.current = target.id
    element.scrollIntoView({ block: 'start' })
  }, [target, mountedChunks])

  // Mounting a chunk above the reader pushes the text down by however far the gap it stood in for
  // was off. The sentence being read was measured before the mount: put it back where it was.
  useLayoutEffect(() => {
    const shift = anchorShift.current
    if (!shift) return

    anchorShift.current = null
    const element = bodyRef.current?.querySelector(`[data-pos="${shift.key}"]`)
    if (!element) return

    const moved = element.getBoundingClientRect().top - shift.top
    if (moved !== 0) window.scrollBy(0, moved)
  }, [mountedChunks])

  // Every gap watches for its own chunk's turn. New gaps replace old ones as the window grows, so
  // this is rebuilt with it.
  useEffect(() => {
    const body = bodyRef.current
    if (!chapter || !body || typeof IntersectionObserver === 'undefined') return

    const observer = new IntersectionObserver(
      (entries) => {
        const opened = entries
          .filter((entry) => entry.isIntersecting)
          .map((entry) => Number((entry.target as HTMLElement).dataset.chunk))

        if (opened.length === 0) return

        const reading = positionRef.current
        const element = reading ? body.querySelector(`[data-pos="${positionKey(reading)}"]`) : null

        if (reading && element && Math.min(...opened) < chunkOfPosition(chapter, reading)) {
          anchorShift.current = { key: positionKey(reading), top: element.getBoundingClientRect().top }
        }

        setChunkWindow((current) => {
          if (!current) return current

          const mounted = new Set(current.mounted)
          opened.forEach((chunk) => mounted.add(chunk))

          return mounted.size === current.mounted.size ? current : { ...current, mounted }
        })
      },
      { rootMargin: GAP_MARGIN },
    )

    body.querySelectorAll('[data-chunk]').forEach((element) => observer.observe(element))
    return () => observer.disconnect()
  }, [chapter, mountedChunks])

  // The top sentence in the reading band is the position. The band's top edge must land exactly
  // one pixel below where scrollIntoView (via Sentence.css's scroll-margin-top) places a target
  // sentence, so the sentence just above the scroll target can never still read as "intersecting"
  // right after a jump — both derive from the same measured header height, so they can't disagree.
  useEffect(() => {
    const body = bodyRef.current
    if (!book || chapterIndex === null || !body || typeof IntersectionObserver === 'undefined') return

    const visible = new Map<string, ReaderPosition>()
    const readingBand = `-${headerHeight + 1}px 0px -50% 0px`

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          const key = (entry.target as HTMLElement).dataset.pos
          const parsed = key ? parsePositionKey(key) : null
          if (!key || !parsed) continue

          if (entry.isIntersecting) visible.set(key, parsed)
          else visible.delete(key)
        }

        const top = [...visible.values()].sort(comparePositions)[0]
        if (!top) return

        setPosition(top)
        report(top)
      },
      { rootMargin: readingBand },
    )

    body.querySelectorAll('[data-pos]').forEach((element) => observer.observe(element))
    return () => observer.disconnect()
  }, [book, chapterIndex, report, headerHeight, mountedChunks])

  const goToChapter = (index: number) => {
    const next = { chapterIndex: index, paragraphIndex: 0, sentenceIndex: 0 }

    setSelection(null)
    setMenuOpen(false)
    setTarget((current) => ({ id: (current?.id ?? 0) + 1, position: next }))
    setPosition(next)
    report(next)
  }

  const onWordTap = useCallback(
    (key: string, tokenIndex: number, resolved: ResolvedWord, form: string, element: HTMLElement) => {
      const countKey = resolveWord(form, EMPTY_STATUSES)?.lemma ?? resolved.lemma
      setSelection({ key, tokenIndex, lemma: resolved.lemma, form, count: counts.get(countKey) ?? 0 })

      // The panel covers the lower part of the screen: bring a word down there up into view.
      if (element.getBoundingClientRect().bottom > window.innerHeight * 0.55) {
        element.scrollIntoView({ block: 'center', behavior: 'smooth' })
      }
    },
    [counts],
  )

  const onStatusChange = useCallback(
    (lemma: string, status: 'learning' | 'known') => setStatuses((previous) => new Map(previous).set(lemma, status)),
    [],
  )

  const onBodyClick = (event: MouseEvent) => {
    if (!(event.target as Element).closest('.reader-word, button')) setSelection(null)
  }

  const changeSettings = (next: ReaderSettings) => {
    setSettings(next)
    saveSettings(next)
  }

  const theme = resolveTheme(settings.theme, prefersDark)
  const style = {
    '--reader-dim': settings.dim,
    '--reader-size': `${TEXT_SIZES[settings.textSize]}px`,
    '--reader-header-height': `${headerHeight}px`,
  } as CSSProperties

  if (!book || !position || !chapter) {
    const message =
      load.status === 'missing'
        ? "This book isn't on this device. Open its file from the library."
        : load.status === 'error'
          ? load.message
          : 'Opening the book…'

    return (
      <div className="reader" data-reader-theme={theme} style={style}>
        <header className="reader-header" ref={headerRef}>
          <button type="button" className="reader-icon-btn" aria-label="Back to the library" onClick={onBack}>
            ‹
          </button>
        </header>
        <p className="reader-state">{message}</p>
      </div>
    )
  }

  const progressStyle = { '--reader-chapter-progress': chapterProgress(book, position) } as CSSProperties

  return (
    <div className="reader" data-reader-theme={theme} style={style}>
      <header className="reader-header" ref={headerRef}>
        <button type="button" className="reader-icon-btn" aria-label="Back to the library" onClick={onBack}>
          ‹
        </button>
        <div className="reader-heading">
          <div className="reader-chapter-title">{chapter.title || `Chapter ${position.chapterIndex + 1}`}</div>
          <div className="reader-book-title">{book.title}</div>
        </div>
        <button type="button" className="reader-icon-btn" aria-label="Contents and display" onClick={() => setMenuOpen(true)}>
          ☰
        </button>
        <div className="reader-progress" style={progressStyle} />
      </header>

      {statusesFailed && <p className="reader-notice">Word highlights unavailable</p>}

      {autoImport.status === 'running' && (
        <p className="reader-notice" role="status">
          Building this book's word list…
          {autoImport.total > 0 && ` ${formatInt(Math.round((autoImport.done / autoImport.total) * 100))} %`}
        </p>
      )}
      {autoImport.status === 'failed' && <p className="reader-notice">Couldn't build this book's word list</p>}

      <main className="reader-body" ref={bodyRef} onClick={onBodyClick}>
        {position.chapterIndex > 0 && (
          <div className="reader-chapter-nav">
            <button type="button" className="btn btn-quiet" onClick={() => goToChapter(position.chapterIndex - 1)}>
              Previous chapter
            </button>
          </div>
        )}

        {chunks.map((chunk, index) =>
          mountedChunks.has(index) ? (
            chunk.map((entry) => (
              <Sentence
                key={entry.key}
                sentence={entry.sentence}
                positionKey={entry.key}
                paragraphStart={entry.paragraphStart}
                statuses={statuses}
                selectedToken={selection?.key === entry.key ? selection.tokenIndex : null}
                canTranslate={canTranslate}
                translation={translations[entry.key]}
                onWordTap={onWordTap}
                onToggleTranslation={toggle}
              />
            ))
          ) : (
            <div
              key={`gap-${index}`}
              className="reader-gap"
              data-chunk={index}
              style={{ '--reader-gap-sentences': chunk.length } as CSSProperties}
              aria-hidden="true"
            />
          ),
        )}

        {position.chapterIndex < book.chapters.length - 1 && (
          <div className="reader-chapter-nav">
            <button type="button" className="btn btn-secondary" onClick={() => goToChapter(position.chapterIndex + 1)}>
              Next chapter
            </button>
          </div>
        )}
      </main>

      {selection && (
        <WordPanel
          key={`${selection.key}:${selection.tokenIndex}`}
          lemma={selection.lemma}
          form={selection.form}
          count={selection.count}
          dictionaryId={server?.dictionaryId ?? null}
          onClose={() => setSelection(null)}
          onStatusChange={onStatusChange}
        />
      )}

      {menuOpen && (
        <ReaderMenu
          chapters={book.chapters}
          currentChapter={position.chapterIndex}
          settings={settings}
          dictionaryId={server?.dictionaryId ?? null}
          onJump={goToChapter}
          onSettings={changeSettings}
          onOpenDictionary={onOpenDictionary}
          onClose={() => setMenuOpen(false)}
        />
      )}
    </div>
  )
}
