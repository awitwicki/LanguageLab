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

  // Прев'ю глав рахується з уже розібраного дерева, тому перемикання
  // рівня вкладеності миттєве — файл вдруге не парситься.
  const chapters = useMemo(() => flattenChapters(sections, mode), [sections, mode])

  // Один воркер на весь час екрана: інакше кожне перемикання рівня
  // пересилало б дерево секцій у новий інстанс.
  useEffect(() => {
    const instance = new Worker(new URL('../worker/parseBook.worker.ts', import.meta.url), {
      type: 'module',
    })

    instance.onmessage = (event: MessageEvent<WorkerResponse>) => pending.current?.(event.data)
    worker.current = instance

    return () => instance.terminate()
  }, [])

  // Слот один, бо UI не дає запустити дві операції одночасно:
  // кнопки заблоковані, поки стадія не повернулась у 'preview'.
  const ask = useCallback(
    (request: WorkerRequest, transfer: Transferable[] = []) =>
      new Promise<WorkerResponse>((resolve) => {
        pending.current = resolve
        worker.current!.postMessage(request, transfer)
      }),
    [],
  )

  // Декодування й розбір XML ідуть тут, у головному потоці, а не у воркері:
  // їм потрібен DOMParser, якого немає у Worker-скоупі в цьому браузері.
  // Вони швидкі — нативний розбір XML багатомегабайтного файлу займає
  // набагато менше секунди, тож вкладка не підвисає.
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

    // Лематизація йде у воркері й тільки тут — після того, як обрано рівень
    // глав. Робити її на кожне перемикання рівня було б нестерпно повільно.
    setStage('aggregating')
    const response = await ask({ kind: 'aggregate', sections, mode })

    if (response.kind !== 'aggregated') {
      setError(response.kind === 'error' ? response.message : 'Несподівана відповідь воркера.')
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
      <h1 className="large-title">Імпорт книжки</h1>

      {error && <p className="error">{error}</p>}

      {stage === 'idle' && (
        <label className="dropzone">
          <input
            type="file"
            accept=".fb2"
            onChange={(e) => e.target.files?.[0] && onFile(e.target.files[0])}
          />
          <strong>Обери файл .fb2</strong>
          <span>або перетягни його сюди</span>
        </label>
      )}

      {stage === 'parsing' && <p className="footnote">Розбираю книжку…</p>}

      {stage !== 'idle' && stage !== 'parsing' && (
        <>
          <label className="field">
            Назва словника
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </label>

          <label className="field">
            Рівень глав
            <select
              value={String(mode)}
              disabled={stage !== 'preview'}
              onChange={(e) => setMode(e.target.value === 'leaf' ? 'leaf' : Number(e.target.value))}
            >
              <option value="leaf">Листкові секції</option>
              {Array.from({ length: maxDepth }, (_, i) => i + 1).map((depth) => (
                <option key={depth} value={depth}>
                  Рівень {depth}
                </option>
              ))}
            </select>
          </label>

          <p className="footnote">
            Знайдено глав: <strong className="num">{chapters.length}</strong>
          </p>

          <ol className="chapter-preview">
            {chapters.map((chapter, index) => (
              <li key={index}>{chapter.title || <em>без назви</em>}</li>
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
              {stage === 'aggregating' && 'Розбираю слова…'}
              {stage === 'uploading' && 'Заливаю…'}
              {stage === 'preview' && 'Імпортувати'}
            </button>
          </div>
        </>
      )}
    </section>
  )
}
