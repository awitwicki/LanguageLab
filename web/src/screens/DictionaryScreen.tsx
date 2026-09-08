import { useEffect, useState } from 'react'
import { api, type DictionaryDetail, type TrainingStarted } from '../api/client'
import { LeitnerScale } from '../components/LeitnerScale'
import { ProgressBar } from '../components/ProgressBar'
import { chaptersLabel, formatInt, percentOf, wordsLabel } from '../lib/format'
import { WHOLE_BOOK, chapterLabel } from '../lib/labels'
import './DictionaryScreen.css'

interface Props {
  id: number
  onSort: (chapterIds: number[] | null, scopeTitle: string) => void
  onTrain: (chapterIds: number[] | null, scopeTitle: string) => void
  onReview: (started: TrainingStarted) => void
}

export function DictionaryScreen({ id, onSort, onTrain, onReview }: Props) {
  const [detail, setDetail] = useState<DictionaryDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reviewBusy, setReviewBusy] = useState(false)
  const [reviewNotice, setReviewNotice] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    // Скидаємо, щоб при перемиканні словника в сайдбарі не блимав попередній.
    setDetail(null)
    setError(null)
    setReviewNotice(null)
    api
      .getDictionary(id)
      .then((d) => {
        if (!cancelled) setDetail(d)
      })
      .catch((e) => {
        if (!cancelled) setError(String(e))
      })
    return () => {
      cancelled = true
    }
  }, [id])

  const startReview = async () => {
    setReviewBusy(true)
    setReviewNotice(null)

    try {
      const started = await api.startReview()

      // 204: між завантаженням екрана й кліком прострочені могли закритись іншою сесією.
      if (!started) {
        setReviewNotice('На сьогодні повторювати нічого.')
        return
      }

      onReview(started)
    } catch (e) {
      setReviewNotice(String(e))
    } finally {
      setReviewBusy(false)
    }
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!detail) {
    return <p className="footnote">Завантажую…</p>
  }

  // Смужка частоти — відносно лідера, щоб топ читався як гістограма, а не як таблиця.
  const maxFrequency = detail.topWords[0]?.frequency ?? 1

  return (
    <>
      <header className="dict-header">
        <h1 className="large-title">{detail.name}</h1>
        <p className="dict-meta num">
          {wordsLabel(detail.wordsCount)}
          {detail.chapters.length > 0 && `, ${chaptersLabel(detail.chapters.length)}`}
        </p>
        <ProgressBar sorted={detail.sortedCount} total={detail.wordsCount} />
        {detail.learning.total > 0 && (
          <div className="dict-learning">
            <span className="footnote">Вивчено</span>
            <LeitnerScale progress={detail.learning} />
          </div>
        )}
      </header>

      <div className="dict-actions">
        <button type="button" className="btn btn-primary" onClick={() => onSort(null, WHOLE_BOOK)}>
          Сортувати всю книжку
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={detail.learnableCount === 0}
          onClick={() => onTrain(null, WHOLE_BOOK)}
        >
          Почати вправу
        </button>
        {detail.dueCount > 0 && (
          <button type="button" className="btn btn-secondary" disabled={reviewBusy} onClick={startReview}>
            Повторити ({formatInt(detail.dueCount)})
          </button>
        )}
      </div>

      {detail.learnableCount === 0 && (
        <p className="footnote dict-actions-hint">
          Немає слів до вивчення: познач слова як «не знаю» під час сортування.
        </p>
      )}

      {reviewNotice && <p className="footnote dict-actions-hint">{reviewNotice}</p>}

      {/* Глави ліворуч (і першими на вузькому екрані), найчастіші слова — праворуч. */}
      <div className="dict-columns">
        {detail.chapters.length > 0 ? (
          <section className="section">
            <h2 className="title">Глави</h2>
            <p className="footnote">Назва глави — сортувати лише її; «Вправа» — тренувати лише її.</p>
            <ul className="chapter-list">
              {detail.chapters.map((chapter) => {
                const label = chapterLabel(chapter)
                const percent = percentOf(chapter.sortedCount, chapter.wordsCount)

                return (
                  <li key={chapter.id} className={`chapter-row${percent === 100 ? ' done' : ''}`}>
                    <button type="button" className="chapter-main" onClick={() => onSort([chapter.id], label)}>
                      <span className="chapter-text">
                        <span className="chapter-title">{label}</span>
                        <span className="chapter-sub num">
                          {wordsLabel(chapter.wordsCount)} · {formatInt(chapter.learnableCount)} до вивчення
                        </span>
                      </span>
                      <span className="chapter-pct num">{percent}%</span>
                      <span className="chevron" aria-hidden="true">
                        ›
                      </span>
                    </button>
                    <button
                      type="button"
                      className="btn btn-quiet chapter-train"
                      disabled={chapter.learnableCount === 0}
                      title={chapter.learnableCount === 0 ? 'У главі немає слів до вивчення' : undefined}
                      aria-label={`Вправа: ${label}`}
                      onClick={() => onTrain([chapter.id], label)}
                    >
                      Вправа
                    </button>
                    {/* Третій рядок — поза кнопкою сортування, щоб шкала не засмічувала її accessible name. */}
                    {chapter.learning.total > 0 && (
                      <div className="chapter-learning">
                        <LeitnerScale progress={chapter.learning} />
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
          </section>
        ) : (
          <p className="footnote">Це плаский словник без глав — сортується лише цілком.</p>
        )}

        {detail.topWords.length > 0 && (
          <section className="section">
            <h2 className="title">Найчастіші слова</h2>
            <ol className="top-words">
              {detail.topWords.map((item, index) => (
                <li key={item.wordPairId} className="top-word">
                  <span className="rank num">{index + 1}</span>
                  <span className="word-cell">
                    <span className="word">{item.word}</span>
                    <span
                      className="bar"
                      style={{ width: `${Math.max(4, Math.round((item.frequency / maxFrequency) * 100))}%` }}
                    />
                  </span>
                  <span className="count num">{formatInt(item.frequency)}</span>
                </li>
              ))}
            </ol>
          </section>
        )}
      </div>
    </>
  )
}
