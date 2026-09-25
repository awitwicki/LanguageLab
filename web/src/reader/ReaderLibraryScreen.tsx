import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react'
import { api, type ReaderBookDto } from '../api/client'
import { formatInt } from '../lib/format'
import type { BookMeta, BookStore } from './bookStore'
import { openBookFile } from './openBook'
import './ReaderLibraryScreen.css'

interface Props {
  store: BookStore
  /** false: IndexedDB is unavailable, books stay only until the tab closes. */
  persistent: boolean
  onOpen: (hash: string) => void
}

function progressLabel(book: ReaderBookDto): string {
  return `Chapter ${formatInt(book.chapterIndex + 1)} of ${formatInt(book.chaptersCount)} · ${formatInt(Math.round(book.progress * 100))} %`
}

function RowMenu({ open, onToggle, actions }: {
  open: boolean
  onToggle: () => void
  actions: { label: string; run: () => void }[]
}) {
  return (
    <div className="library-row-menu">
      <button type="button" className="btn btn-quiet" aria-label="More actions" aria-expanded={open} onClick={onToggle}>
        ⋯
      </button>
      {open && (
        <div className="library-row-actions">
          {actions.map((action) => (
            <button key={action.label} type="button" className="btn btn-quiet" onClick={action.run}>
              {action.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

export function ReaderLibraryScreen({ store, persistent, onOpen }: Props) {
  const [local, setLocal] = useState<BookMeta[] | null>(null)
  const [server, setServer] = useState<ReaderBookDto[]>([])
  const [serverFailed, setServerFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [continuing, setContinuing] = useState<ReaderBookDto | null>(null)
  const [mismatch, setMismatch] = useState<{ file: File; expected: ReaderBookDto } | null>(null)
  const [menuFor, setMenuFor] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    store
      .list()
      .then(setLocal)
      .catch(() => setLocal([]))

    api
      .listReaderBooks()
      .then((books) => {
        setServer(books)
        setServerFailed(false)
      })
      .catch(() => setServerFailed(true))
  }, [store])

  useEffect(() => {
    reload()
  }, [reload])

  const pick = (book: ReaderBookDto | null) => {
    setContinuing(book)
    setError(null)
    setMismatch(null)
    input.current?.click()
  }

  const open = async (file: File, expected: ReaderBookDto | null) => {
    setBusy(true)
    setError(null)

    const result = await openBookFile(file, store, expected?.fileHash ?? null)

    setBusy(false)

    if (result.kind === 'opened') onOpen(result.hash)
    else if (result.kind === 'mismatch' && expected) setMismatch({ file, expected })
    else if (result.kind === 'error') setError(result.message)
  }

  const onFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ''

    if (file) void open(file, continuing)
  }

  const removeFromDevice = async (hash: string) => {
    setMenuFor(null)
    await store.remove(hash).catch(() => undefined)
    reload()
  }

  const removeFromLibrary = async (hash: string) => {
    setMenuFor(null)
    await store.remove(hash).catch(() => undefined)

    try {
      await api.removeReaderBook(hash)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }

    reload()
  }

  const serverByHash = new Map(server.map((book) => [book.fileHash, book]))
  const localHashes = new Set((local ?? []).map((book) => book.hash))
  const lastRead = (book: BookMeta) => serverByHash.get(book.hash)?.updatedAt ?? book.addedAt
  const onDevice = [...(local ?? [])].sort((a, b) => lastRead(b).localeCompare(lastRead(a)))
  const elsewhere = server.filter((book) => !localHashes.has(book.fileHash))

  return (
    <div className="library">
      <header className="library-head">
        <h1 className="title">Reading</h1>
        <button type="button" className="btn btn-primary" disabled={busy} onClick={() => pick(null)}>
          Open a book
        </button>
        <input ref={input} type="file" accept=".fb2" hidden onChange={onFileChange} />
      </header>

      {!persistent && (
        <p className="library-notice">
          This browser can't keep books on this device — a book stays open only until you close the tab.
        </p>
      )}

      {error && (
        <p className="library-error" role="alert">
          {error}
        </p>
      )}

      {mismatch && (
        <div className="library-mismatch" role="alert">
          <p>This is a different file than the one you read before ("{mismatch.expected.title}").</p>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              const { file } = mismatch
              setMismatch(null)
              void open(file, null)
            }}
          >
            Open as a new book
          </button>
        </div>
      )}

      <section data-section="device">
        <h2 className="library-section">On this device</h2>
        {local === null ? (
          <p className="library-empty">Loading…</p>
        ) : onDevice.length === 0 ? (
          <p className="library-empty">No books on this device yet. Open an fb2 file to start reading.</p>
        ) : (
          <ul className="library-list">
            {onDevice.map((book) => {
              const record = serverByHash.get(book.hash)

              return (
                <li key={book.hash} className="library-row">
                  <button type="button" className="library-row-open" onClick={() => onOpen(book.hash)}>
                    <span className="library-row-title">{book.title}</span>
                    {book.author && <span className="library-row-meta">{book.author}</span>}
                    <span className="library-row-meta num">{record ? progressLabel(record) : 'Not started'}</span>
                  </button>
                  <RowMenu
                    open={menuFor === book.hash}
                    onToggle={() => setMenuFor(menuFor === book.hash ? null : book.hash)}
                    actions={[
                      { label: 'Remove from this device', run: () => void removeFromDevice(book.hash) },
                      { label: 'Remove from library', run: () => void removeFromLibrary(book.hash) },
                    ]}
                  />
                </li>
              )
            })}
          </ul>
        )}
      </section>

      {elsewhere.length > 0 && (
        <section data-section="elsewhere">
          <h2 className="library-section">On other devices</h2>
          <ul className="library-list">
            {elsewhere.map((book) => (
              <li key={book.fileHash} className="library-row">
                <div className="library-row-main">
                  <span className="library-row-title">{book.title}</span>
                  <span className="library-row-meta num">{progressLabel(book)}</span>
                </div>
                <button type="button" className="btn btn-secondary" disabled={busy} onClick={() => pick(book)}>
                  Open the file to continue
                </button>
                <RowMenu
                  open={menuFor === book.fileHash}
                  onToggle={() => setMenuFor(menuFor === book.fileHash ? null : book.fileHash)}
                  actions={[{ label: 'Remove from library', run: () => void removeFromLibrary(book.fileHash) }]}
                />
              </li>
            ))}
          </ul>
        </section>
      )}

      {serverFailed && (
        <p className="library-notice">Couldn't load your library from the server. Books on this device still open.</p>
      )}
    </div>
  )
}
