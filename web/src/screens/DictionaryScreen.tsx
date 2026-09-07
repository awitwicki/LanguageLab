import { useEffect, useState } from 'react'
import { api, type DictionaryDetail } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { chaptersLabel, formatInt, percentOf, wordsLabel } from '../lib/format'
import { WHOLE_BOOK, chapterLabel } from '../lib/labels'
import './DictionaryScreen.css'

interface Props {
  id: number
  onSort: (chapterIds: number[] | null, scopeTitle: string) => void
}

export function DictionaryScreen({ id, onSort }: Props) {
  const [detail, setDetail] = useState<DictionaryDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    // Скидаємо, щоб при перемиканні словника в сайдбарі не блимав попередній.
    setDetail(null)
    setError(null)
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
      </header>

      <div className="dict-actions">
        <button type="button" className="btn btn-primary" onClick={() => onSort(null, WHOLE_BOOK)}>
          Сортувати всю книжку
        </button>
        {/* Тренування у вебі ще немає — див. README «TODO». Кнопка стоїть, щоб макет був фінальний. */}
        <button type="button" className="btn btn-secondary" disabled title="Скоро: тренування у вебі">
          Почати вправу
        </button>
      </div>

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

      {detail.chapters.length > 0 ? (
        <section className="section">
          <h2 className="title">Глави</h2>
          <p className="footnote">Натисни на главу, щоб сортувати лише її.</p>
          <div className="chapter-list">
            {detail.chapters.map((chapter) => {
              const label = chapterLabel(chapter)
              const percent = percentOf(chapter.sortedCount, chapter.wordsCount)

              return (
                <button
                  key={chapter.id}
                  type="button"
                  className={`chapter-row${percent === 100 ? ' done' : ''}`}
                  onClick={() => onSort([chapter.id], label)}
                >
                  <span className="chapter-text">
                    <span className="chapter-title">{label}</span>
                    <span className="chapter-sub num">{wordsLabel(chapter.wordsCount)}</span>
                  </span>
                  <span className="chapter-pct num">{percent}%</span>
                  <span className="chevron" aria-hidden="true">
                    ›
                  </span>
                </button>
              )
            })}
          </div>
        </section>
      ) : (
        <p className="footnote">Це плаский словник без глав — сортується лише цілком.</p>
      )}
    </>
  )
}
