import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { decodeFb2 } from '../fb2/decode'
import { flattenChapters, parseBook, type ChapterMode, type SectionNode } from '../fb2/chapters'
import type { WorkerRequest, WorkerResponse } from '../worker/parseBook.worker'
import { api } from '../api/client'
import { openOutsideTelegram, telegramInitData } from '../auth/telegram'
import { ProgressBar } from '../components/ProgressBar'
import { formatBytes, formatInt, percentOf } from '../lib/format'
import './ImportScreen.css'

interface Props {
  onImported: (dictionaryId: number) => void
}

type Stage = 'idle' | 'parsing' | 'preview' | 'aggregating' | 'uploading'

/** The request the worker is answering: settled by its final message, or by its failure. */
interface Pending {
  resolve: (response: WorkerResponse) => void
  reject: (error: Error) => void
}

/** Chapters lemmatized so far; null until the worker has picked the request up. */
type Extraction = { done: number; total: number } | null

/** Bytes of the JSON body the browser has pushed out; null until it reports the first chunk. */
type Upload = { sent: number; total: number } | null

export function ImportScreen({ onImported }: Props) {
  const [stage, setStage] = useState<Stage>('idle')
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [sections, setSections] = useState<SectionNode[]>([])
  const [maxDepth, setMaxDepth] = useState(1)
  const [mode, setMode] = useState<ChapterMode>('leaf')
  const [isPublic, setIsPublic] = useState(true)
  const [extraction, setExtraction] = useState<Extraction>(null)
  const [upload, setUpload] = useState<Upload>(null)

  const worker = useRef<Worker | null>(null)
  const pending = useRef<Pending | null>(null)

  // The chapter preview is computed from the already parsed tree, so switching
  // the nesting level is instant — the file is not parsed a second time.
  const chapters = useMemo(() => flattenChapters(sections, mode), [sections, mode])

  // One worker for the screen's whole lifetime: otherwise every level switch
  // would ship the section tree to a fresh instance.
  useEffect(() => {
    const instance = new Worker(new URL('../worker/parseBook.worker.ts', import.meta.url), {
      type: 'module',
    })

    const settle = (outcome: (request: Pending) => void) => {
      const request = pending.current
      pending.current = null

      if (request) {
        outcome(request)
      }
    }

    instance.onmessage = (event: MessageEvent<WorkerResponse>) => {
      if (event.data.kind === 'progress') {
        setExtraction({ done: event.data.done, total: event.data.total })
        return
      }

      settle((request) => request.resolve(event.data))
    }

    // A worker that never loads (a stale bundle after a deploy, a download that broke off on
    // mobile data) or dies mid-book (memory, on phones) never answers. Without these two
    // handlers the screen would say "Extracting words…" forever, with nothing to act on.
    instance.onerror = (event) => {
      const detail = event.message ? ` (${event.message})` : ''

      settle((request) =>
        request.reject(new Error(`The word extractor failed${detail}. Reload the app and try again.`)),
      )
    }

    instance.onmessageerror = () =>
      settle((request) =>
        request.reject(new Error('The word extractor sent an unreadable reply. Reload the app and try again.')),
      )

    worker.current = instance

    return () => instance.terminate()
  }, [])

  // A single slot, because the UI does not allow two operations at once:
  // the buttons are disabled until the stage returns to 'preview'.
  const ask = useCallback(
    (request: WorkerRequest) =>
      new Promise<WorkerResponse>((resolve, reject) => {
        pending.current = { resolve, reject }
        worker.current!.postMessage(request)
      }),
    [],
  )

  // Decoding and XML parsing happen here, on the main thread, not in the worker:
  // they need DOMParser, which the Worker scope lacks in this browser.
  // They are fast — native XML parsing of a multi-megabyte file takes far
  // less than a second, so the tab does not freeze.
  const onFile = useCallback((file: File) => {
    setStage('parsing')
    setError(null)

    file
      .arrayBuffer()
      .then((buffer) => {
        const xml = decodeFb2(buffer)
        const { bookTitle, sections, maxDepth } = parseBook(xml)

        setSections(sections)
        setMaxDepth(maxDepth)
        setName(bookTitle || file.name.replace(/\.fb2$/i, ''))
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

    let response: WorkerResponse

    try {
      response = await ask({ kind: 'aggregate', sections, mode })
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStage('preview')
      return
    }

    if (response.kind !== 'aggregated') {
      setError(response.kind === 'error' ? response.message : 'Unexpected response from the worker.')
      setStage('preview')
      return
    }

    setStage('uploading')
    setUpload(null)

    try {
      const result = await api.importDictionary(
        {
          name,
          isPublic,
          chapters: response.chapters.map((c) => ({
            order: c.order,
            title: c.title,
            words: c.words,
          })),
        },
        (sent, total) => setUpload({ sent, total }),
      )

      onImported(result.dictionaryId)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStage('preview')
    }
  }, [ask, sections, mode, name, isPublic, onImported])

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
            accept=".fb2"
            onChange={(e) => e.target.files?.[0] && onFile(e.target.files[0])}
          />
          <strong>Choose an .fb2 file</strong>
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

          <p className="footnote">
            Chapters found: <strong className="num">{chapters.length}</strong>
          </p>

          <ol className="chapter-preview">
            {chapters.map((chapter, index) => (
              <li key={index}>{chapter.title || <em>untitled</em>}</li>
            ))}
          </ol>

          <label className="field checkbox">
            <input
              type="checkbox"
              checked={isPublic}
              disabled={stage !== 'preview'}
              onChange={(e) => setIsPublic(e.target.checked)}
            />
            Visible to all users
          </label>

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

          {working && (
            <ImportStatus stage={stage} extraction={extraction} upload={upload} />
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
