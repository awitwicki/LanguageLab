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
  /** An uploader or admin: opening a book with no dictionary builds one. */
  canImport: boolean
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

export function ReaderScreen({ hash, store, canImport, onBack, onOpenDictionary }: Props) {
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
  const scrollTarget = useRef<string | null>(null)
  const [headerHeight, setHeaderHeight] = useState(DEFAULT_HEADER_HEIGHT)

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
    enabled: canImport && telegramInitData() === null && server != null && server.dictionaryId === null,
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

        const fileName = metas.find((meta) => meta.hash === hash)?.fileName ?? 'book.fb2'

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
    scrollTarget.current = positionKey(start)
    setPosition(start)
  }, [book, server, position, hash])

  const chapterIndex = position?.chapterIndex ?? null

  useLayoutEffect(() => {
    const key = scrollTarget.current
    if (!key || !bodyRef.current) return

    scrollTarget.current = null
    bodyRef.current.querySelector(`[data-pos="${key}"]`)?.scrollIntoView({ block: 'start' })
  }, [chapterIndex, book])

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
  }, [book, chapterIndex, report, headerHeight])

  const goToChapter = (index: number) => {
    const target = { chapterIndex: index, paragraphIndex: 0, sentenceIndex: 0 }

    setSelection(null)
    setMenuOpen(false)

    if (chapterIndex === index) {
      bodyRef.current?.querySelector('[data-pos]')?.scrollIntoView({ block: 'start' })
      return
    }

    scrollTarget.current = positionKey(target)
    setPosition(target)
    report(target)
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

  if (!book || !position) {
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

  const chapter = book.chapters[position.chapterIndex]
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

        {chapter.paragraphs.map((paragraph, paragraphIndex) =>
          paragraph.sentences.map((sentence, sentenceIndex) => {
            const key = positionKey({ chapterIndex: position.chapterIndex, paragraphIndex, sentenceIndex })

            return (
              <Sentence
                key={key}
                sentence={sentence}
                positionKey={key}
                paragraphStart={sentenceIndex === 0 && paragraphIndex > 0}
                statuses={statuses}
                selectedToken={selection?.key === key ? selection.tokenIndex : null}
                canTranslate={canTranslate}
                translation={translations[key]}
                onWordTap={onWordTap}
                onToggleTranslation={toggle}
              />
            )
          }),
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
