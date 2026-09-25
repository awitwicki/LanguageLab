import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { readBookSource, stripBookExtension } from '../books/format'
import { toParsedBook } from '../books/toParsedBook'
import { flattenChapters, type ChapterMode, type SectionNode } from '../fb2/chapters'
import { createWordExtractor, type WordExtractor } from '../fb2/wordExtractor'
import type { AggregatedChapter } from '../fb2/aggregate'
import { api, type ImportResult, type UserRole } from '../api/client'
import type { BookStore } from '../reader/bookStore'
import { sha256Hex } from '../reader/hash'
import { addBookToReader } from '../reader/openBook'
import { openOutsideTelegram, telegramInitData } from '../auth/telegram'
import { canPublishDirectly } from '../auth/roles'
import { ProgressBar } from '../components/ProgressBar'
import { formatBytes, formatInt, percentOf } from '../lib/format'
import './ImportScreen.css'

interface Props {
  onImported: (dictionaryId: number) => void
  /** Decides whether the publication checkbox publishes directly or only requests review. */
  role: UserRole
  /** The reader's store: an imported book is also put in the reader. Absent in tests of the import alone. */
  bookStore?: BookStore
}

type Stage = 'idle' | 'parsing' | 'preview' | 'aggregating' | 'uploading' | 'done'

/** Chapters lemmatized so far; null until the worker has picked the request up. */
type Extraction = { done: number; total: number } | null

/** Bytes of the JSON body the browser has pushed out; null until it reports the first chunk. */
type Upload = { sent: number; total: number } | null

export function ImportScreen({ onImported, role, bookStore }: Props) {
  const [stage, setStage] = useState<Stage>('idle')
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [sections, setSections] = useState<SectionNode[]>([])
  const [maxDepth, setMaxDepth] = useState(1)
  const [mode, setMode] = useState<ChapterMode>('leaf')
  const [requestPublication, setRequestPublication] = useState(false)
  const [extraction, setExtraction] = useState<Extraction>(null)
  const [upload, setUpload] = useState<Upload>(null)
  const [fileHash, setFileHash] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)

  const extractor = useRef<WordExtractor | null>(null)
  // The chosen file, kept for the reader once the import succeeds.
  const file = useRef<{ bytes: ArrayBuffer; name: string } | null>(null)

  // The chapter preview is computed from the already parsed tree, so switching
  // the nesting level is instant — the file is not parsed a second time.
  const chapters = useMemo(() => flattenChapters(sections, mode), [sections, mode])

  // One worker for the screen's whole lifetime: otherwise every level switch would ship the
  // section tree to a fresh instance.
  useEffect(() => {
    const instance = createWordExtractor()
    extractor.current = instance

    return () => instance.dispose()
  }, [])

  // Decoding, unzipping (for an epub) and XML parsing happen here, on the main thread, not in the
  // worker: they need DOMParser, which the Worker scope lacks in this browser. They are all fast —
  // native XML parsing and fflate's unzipSync over a multi-megabyte file take far less than a
  // second, so the tab does not freeze.
  const onFile = useCallback((chosen: File) => {
    setStage('parsing')
    setError(null)

    chosen
      .arrayBuffer()
      .then(async (buffer) => {
        file.current = { bytes: buffer, name: chosen.name }
        const { bookTitle, sections, maxDepth } = toParsedBook(readBookSource(buffer, chosen.name))

        setSections(sections)
        setMaxDepth(maxDepth)
        setName(bookTitle || stripBookExtension(chosen.name))
        setFileHash(await sha256Hex(buffer))
        setStage('preview')
      })
      .catch((e) => {
        setError(e instanceof Error ? e.message : String(e))
        setStage('idle')
      })
  }, [])

  const onUpload = useCallback(async () => {
    setError(null)

    // Lemmatization runs in the worker and only here — after the chapter level
    // is chosen. Doing it on every level switch would be unbearably slow.
    setStage('aggregating')
    setExtraction(null)

    let extracted: AggregatedChapter[]

    try {
      extracted = await extractor.current!.extract(sections, mode, (done, total) => setExtraction({ done, total }))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStage('preview')
      return
    }

    setStage('uploading')
    setUpload(null)

    try {
      const importResult = await api.importDictionary(
        {
          name,
          requestPublication,
          fileHash: fileHash ?? undefined,
          chapters: extracted.map((c) => ({
            order: c.order,
            title: c.title,
            words: c.words,
          })),
        },
        (sent, total) => setUpload({ sent, total }),
      )

      // The same file, straight into the reader. Best effort and fire-and-forget: the import
      // itself already succeeded, and waiting here would re-parse the book and write
      // IndexedDB, stalling the screen at "uploading 100%".
      if (bookStore && file.current && fileHash) {
        void addBookToReader(bookStore, file.current.bytes, file.current.name, fileHash).catch(() => undefined)
      }

      setResult(importResult)
      setStage('done')
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStage('preview')
    }
  }, [sections, mode, name, requestPublication, fileHash, onImported, bookStore])

  const working = stage === 'aggregating' || stage === 'uploading'

  // Inside Telegram's webview the word extractor never starts: the request is posted and
  // nothing comes back, no progress, no error. Rather than let the screen hang, send the
  // importer to a real browser. The README backlog holds the open question of why.
  const insideTelegram = telegramInitData() !== null

  return (
    <section className="import">
      <h1 className="large-title">Import a book</h1>

      {error && <p className="error">{error}</p>}

      {stage === 'idle' && insideTelegram && (
        <div className="import-notice">
          <p>
            Importing a book works in a regular browser, not inside Telegram: the word
            extractor does not start here. Open LanguageLab in your browser and import from there.
          </p>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => openOutsideTelegram(window.location.href)}
          >
            Open in browser
          </button>
        </div>
      )}

      {stage === 'idle' && !insideTelegram && (
        <label className="dropzone">
          <input
            type="file"
            accept=".fb2,.epub,.zip"
            onChange={(e) => e.target.files?.[0] && onFile(e.target.files[0])}
          />
          <strong>Choose an .fb2 or .epub file</strong>
          <span>or drop it here</span>
        </label>
      )}

      {stage === 'parsing' && <p className="footnote">Reading the book…</p>}

      {stage !== 'idle' && stage !== 'parsing' && (
        <>
          <label className="field">
            Dictionary name
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </label>

          {maxDepth > 1 && (
            <label className="field">
              Chapter level
              <select
                value={String(mode)}
                disabled={stage !== 'preview'}
                onChange={(e) => setMode(e.target.value === 'leaf' ? 'leaf' : Number(e.target.value))}
              >
                <option value="leaf">Leaf sections</option>
                {Array.from({ length: maxDepth }, (_, i) => i + 1).map((depth) => (
                  <option key={depth} value={depth}>
                    Level {depth}
                  </option>
                ))}
              </select>
            </label>
          )}

          <p className="footnote">
            Chapters found: <strong className="num">{chapters.length}</strong>
          </p>

          <ol className="chapter-preview">
            {chapters.map((chapter, index) => (
              <li key={index}>{chapter.title || <em>untitled</em>}</li>
            ))}
          </ol>

          {stage !== 'done' && (
            <>
              <label className="field checkbox">
                <input
                  type="checkbox"
                  checked={requestPublication}
                  disabled={stage !== 'preview'}
                  onChange={(e) => setRequestPublication(e.target.checked)}
                />
                {canPublishDirectly(role) ? 'Visible to all users' : 'Submit for review after import'}
              </label>

              {!canPublishDirectly(role) && (
                <p className="footnote">An administrator checks the dictionary before other users see it.</p>
              )}

              <div>
                <button
                  type="button"
                  className="btn btn-primary btn-lg"
                  disabled={stage !== 'preview' || chapters.length === 0}
                  onClick={onUpload}
                >
                  {stage === 'aggregating' && 'Extracting words…'}
                  {stage === 'uploading' && 'Uploading…'}
                  {stage === 'preview' && 'Import'}
                </button>
              </div>
            </>
          )}

          {working && (
            <ImportStatus stage={stage} extraction={extraction} upload={upload} />
          )}

          {result && (
            <>
              <p className="footnote">
                Dictionary created: <strong className="num">{formatInt(result.totalWords)}</strong> unique words
              </p>
              {result.droppedWords > 0 && (
                <p className="footnote">
                  Skipped: <strong className="num">{formatInt(result.droppedWords)}</strong> entries that are not English words.
                </p>
              )}
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => onImported(result.dictionaryId)}
              >
                Continue
              </button>
            </>
          )}
        </>
      )}
    </section>
  )
}

interface StatusProps {
  stage: 'aggregating' | 'uploading'
  extraction: Extraction
  upload: Upload
}

/**
 * Where the import is right now. A whole book takes a phone a while to lemmatize and then to
 * push out over mobile data — without this the button label alone reads as "stuck".
 */
function ImportStatus({ stage, extraction, upload }: StatusProps) {
  const { text, done, total } = describeStatus(stage, extraction, upload)

  return (
    <div className="import-status" role="status">
      <p className="footnote">{text}</p>
      <ProgressBar sorted={done} total={total} showLabel={false} />
    </div>
  )
}

function describeStatus(stage: StatusProps['stage'], extraction: Extraction, upload: Upload) {
  if (stage === 'aggregating') {
    if (!extraction) {
      return { text: 'Starting the word extractor…', done: 0, total: 1 }
    }

    return {
      text: `Extracting words: chapter ${formatInt(extraction.done)} of ${formatInt(extraction.total)}…`,
      done: extraction.done,
      total: extraction.total,
    }
  }

  if (!upload) {
    return { text: 'Uploading…', done: 0, total: 1 }
  }

  if (upload.sent < upload.total) {
    return {
      text: `Uploading ${formatBytes(upload.total)}: ${percentOf(upload.sent, upload.total)}%`,
      done: upload.sent,
      total: upload.total,
    }
  }

  // Every byte is out; what remains is the server writing the book down.
  return { text: 'Saving on the server…', done: upload.total, total: upload.total }
}
