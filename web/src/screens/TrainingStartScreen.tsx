import { useState } from 'react'
import { api, type TrainingStarted } from '../api/client'
import { LeitnerScale } from '../components/LeitnerScale'
import { formatInt, wordsLabel } from '../lib/format'
import { useBatchPreview } from '../training/useBatchPreview'
import './TrainingStartScreen.css'

const BATCH_SIZES = [5, 10, 20] as const
export const DEFAULT_BATCH_SIZE = 10

const NO_WORDS = 'У цьому наборі немає слів до вивчення — познач слова як «не знаю» під час сортування.'

interface Props {
  dictionaryId: number
  dictionaryName: string
  chapterIds: number[] | null
  scopeTitle: string
  onStarted: (started: TrainingStarted, batchSize: number) => void
  onBack: () => void
}

export function TrainingStartScreen({ dictionaryId, dictionaryName, chapterIds, scopeTitle, onStarted, onBack }: Props) {
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const { preview, rows, batchIds, batchSize, setBatchSize, crossOut, bringBack, pendingId, error } = useBatchPreview({
    dictionaryId,
    chapterIds,
    initialBatchSize: DEFAULT_BATCH_SIZE,
  })

  const learnableCount = preview?.learnableCount ?? 0
  const inChapter = chapterIds !== null && chapterIds.length > 0

  const start = async () => {
    setBusy(true)
    setNotice(null)

    try {
      const started = await api.startNewBatch(dictionaryId, chapterIds, batchSize, batchIds)

      // 204: між відкриттям екрана й кліком слова могли скінчитись — це не помилка.
      if (!started) {
        setNotice(NO_WORDS)
        return
      }

      onStarted(started, batchSize)
    } catch (e) {
      setNotice(String(e))
    } finally {
      setBusy(false)
    }
  }

  // Номер лише в активних рядках: викреслені в батч не входять.
  let rank = 0

  return (
    <section className="training-start">
      <div className="training-start-nav">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ {dictionaryName}
        </button>
      </div>

      <p className="footnote">
        {dictionaryName} · {scopeTitle}
      </p>
      <h1 className="large-title">Скільки слів?</h1>

      {error && <p className="error">{error}</p>}
      {!preview && !error && <p className="footnote">Завантажую…</p>}

      {preview && (
        <>
          <LeitnerScale progress={preview.learning} size="large" />
          <p className="training-start-hint num">{wordsLabel(learnableCount)} до вивчення в цьому наборі</p>
        </>
      )}

      <div className="segment" role="radiogroup" aria-label="Розмір батча">
        {BATCH_SIZES.map((size) => (
          <button
            key={size}
            type="button"
            role="radio"
            aria-checked={size === batchSize}
            className={`segment-item num${size === batchSize ? ' is-active' : ''}`}
            onClick={() => setBatchSize(size)}
          >
            {size}
          </button>
        ))}
      </div>

      {learnableCount > 0 && learnableCount < batchSize && (
        <p className="footnote">Слів менше, ніж обрано — батч буде з {wordsLabel(learnableCount)}.</p>
      )}

      {preview && learnableCount === 0 && <p className="footnote training-start-notice">{NO_WORDS}</p>}

      {rows.length > 0 && (
        <div className="batch-preview">
          <p className="footnote">Слова батча · частота {inChapter ? 'в главі' : 'в книжці'}</p>
          <ol className="batch-preview-list">
            {rows.map((row) => {
              const { wordPairId, word, translation, frequency } = row.candidate
              const number = row.struck ? null : ++rank

              return (
                <li key={wordPairId} className={`batch-row${row.struck ? ' is-struck' : ''}`}>
                  <span className="batch-rank num">{number ?? '—'}</span>
                  <span className="batch-word">
                    <span className="batch-word-text">{word}</span>
                    <span className="footnote">{translation}</span>
                  </span>
                  <span className="batch-freq footnote num">{formatInt(frequency)}</span>
                  {row.struck ? (
                    <button
                      type="button"
                      className="btn btn-quiet"
                      disabled={pendingId !== null}
                      onClick={() => bringBack(wordPairId)}
                    >
                      Повернути
                    </button>
                  ) : (
                    <button
                      type="button"
                      className="btn btn-quiet batch-know"
                      aria-label={`Знаю: ${word}`}
                      title="Знаю це слово"
                      disabled={pendingId !== null}
                      onClick={() => crossOut(wordPairId)}
                    >
                      ×
                    </button>
                  )}
                </li>
              )
            })}
          </ol>
        </div>
      )}

      {notice && <p className="footnote training-start-notice">{notice}</p>}

      <div>
        <button
          type="button"
          className="btn btn-primary btn-lg"
          disabled={busy || preview === null || batchIds.length === 0}
          onClick={start}
        >
          Почати
        </button>
      </div>
    </section>
  )
}
