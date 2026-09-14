import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { decodeFb2 } from '../fb2/decode'
import { flattenChapters, parseBook, type ChapterMode, type SectionNode } from '../fb2/chapters'
import type { WorkerRequest, WorkerResponse } from '../worker/parseBook.worker'
import { api } from '../api/client'
import './ImportScreen.css'

interface Props {
  onImported: (dictionaryId: number) => void
}

type Stage = 'idle' | 'parsing' | 'preview' | 'aggregating' | 'uploading'

export function ImportScreen({ onImported }: Props) {
  const [stage, setStage] = useState<Stage>('idle')
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [sections, setSections] = useState<SectionNode[]>([])
  const [maxDepth, setMaxDepth] = useState(1)
  const [mode, setMode] = useState<ChapterMode>('leaf')
  const [isPublic, setIsPublic] = useState(true)

  const worker = useRef<Worker | null>(null)
  const pending = useRef<((response: WorkerResponse) => void) | null>(null)

  // The chapter preview is computed from the already parsed tree, so switching
  // the nesting level is instant — the file is not parsed a second time.
  const chapters = useMemo(() => flattenChapters(sections, mode), [sections, mode])

  // One worker for the screen's whole lifetime: otherwise every level switch
  // would ship the section tree to a fresh instance.
  useEffect(() => {
    const instance = new Worker(new URL('../worker/parseBook.worker.ts', import.meta.url), {
      type: 'module',
    })

    instance.onmessage = (event: MessageEvent<WorkerResponse>) => pending.current?.(event.data)
    worker.current = instance

    return () => instance.terminate()
  }, [])

  // A single slot, because the UI does not allow two operations at once:
  // the buttons are disabled until the stage returns to 'preview'.
  const ask = useCallback(
    (request: WorkerRequest, transfer: Transferable[] = []) =>
      new Promise<WorkerResponse>((resolve) => {
        pending.current = resolve
        worker.current!.postMessage(request, transfer)
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
    const response = await ask({ kind: 'aggregate', sections, mode })

    if (response.kind !== 'aggregated') {
      setError(response.kind === 'error' ? response.message : 'Unexpected response from the worker.')
      setStage('preview')
      return
    }

    setStage('uploading')

    try {
      const result = await api.importDictionary({
        name,
        isPublic,
        chapters: response.chapters.map((c) => ({
          order: c.order,
          title: c.title,
          words: c.words,
        })),
      })

      onImported(result.dictionaryId)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStage('preview')
    }
  }, [ask, sections, mode, name, isPublic, onImported])

  return (
    <section className="import">
      <h1 className="large-title">Import a book</h1>

      {error && <p className="error">{error}</p>}

      {stage === 'idle' && (
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
        </>
      )}
    </section>
  )
}
