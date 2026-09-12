import { useEffect, useState } from 'react'
import { api, type ChapterView, type DictionaryDetail, type TrainingStarted, type UserRole } from '../api/client'
import { LeitnerScale } from '../components/LeitnerScale'
import { ProgressBar } from '../components/ProgressBar'
import { chaptersLabel, formatDue, formatInt, percentOf, wordsLabel } from '../lib/format'
import { WHOLE_BOOK, chapterLabel } from '../lib/labels'
import { chapterAction } from '../lib/learning'
import './DictionaryScreen.css'

interface Props {
  id: number
  role: UserRole
  onSort: (chapterIds: number[] | null, scopeTitle: string) => void
  onTrain: (chapterIds: number[] | null, scopeTitle: string) => void
  /** scopeTitle is the chapter for a chapter review; absent for the global one. */
  onReview: (started: TrainingStarted, scopeTitle?: string) => void
}

export function DictionaryScreen({ id, role, onSort, onTrain, onReview }: Props) {
  const [detail, setDetail] = useState<DictionaryDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reviewBusy, setReviewBusy] = useState(false)
  const [reviewNotice, setReviewNotice] = useState<string | null>(null)
  const [visibilityBusy, setVisibilityBusy] = useState(false)
  const [visibilityError, setVisibilityError] = useState<string | null>(null)

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

  // One path for both the header's global review and a chapter's own: the scope is the
  // only difference, and a chapter's 204 is the same race as the global one.
  const startReview = async (chapter?: ChapterView) => {
    setReviewBusy(true)
    setReviewNotice(null)

    try {
      const started = await api.startReview(chapter ? { dictionaryId: id, chapterIds: [chapter.id] } : undefined)

      // 204: між завантаженням екрана й кліком прострочені могли закритись іншою сесією.
      if (!started) {
        setReviewNotice('Nothing to review today.')
        return
      }

      onReview(started, chapter ? chapterLabel(chapter) : undefined)
    } catch (e) {
      setReviewNotice(String(e))
    } finally {
      setReviewBusy(false)
    }
  }

  const toggleVisibility = async (isPublic: boolean) => {
    if (!detail) return
    const previous = detail.isPublic
    setVisibilityBusy(true)
    setVisibilityError(null)
    setDetail({ ...detail, isPublic })

    try {
      await api.setDictionaryVisibility(id, isPublic)
    } catch (e) {
      setDetail((d) => (d ? { ...d, isPublic: previous } : d))
      setVisibilityError(String(e))
    } finally {
      setVisibilityBusy(false)
    }
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!detail) {
    return <p className="footnote">Loading…</p>
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
        {role === 'admin' && (
          <label className="dict-visibility">
            <input
              type="checkbox"
              checked={detail.isPublic}
              disabled={visibilityBusy}
              onChange={(e) => void toggleVisibility(e.target.checked)}
            />
            Public
          </label>
        )}
        {visibilityError && <p className="footnote error">{visibilityError}</p>}
        <ProgressBar sorted={detail.sortedCount} total={detail.wordsCount} />
        {detail.learning.total > 0 && (
          <div className="dict-learning">
            <span className="footnote">Learned</span>
            <LeitnerScale progress={detail.learning} />
          </div>
        )}
      </header>

      <div className="dict-actions">
        <button type="button" className="btn btn-primary" onClick={() => onSort(null, WHOLE_BOOK)}>
          Sort the whole book
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={detail.learnableCount === 0}
          onClick={() => onTrain(null, WHOLE_BOOK)}
        >
          Start exercise
        </button>
        {detail.dueCount > 0 && (
          <button type="button" className="btn btn-secondary" disabled={reviewBusy} onClick={() => void startReview()}>
            Review ({formatInt(detail.dueCount)})
          </button>
        )}
      </div>

      {detail.learnableCount === 0 && (
        <p className="footnote dict-actions-hint">
          No words to learn yet: mark words as “don’t know” while sorting.
        </p>
      )}

      {reviewNotice && <p className="footnote dict-actions-hint">{reviewNotice}</p>}

      {/* Глави ліворуч (і першими на вузькому екрані), найчастіші слова — праворуч. */}
      <div className="dict-columns">
        {detail.chapters.length > 0 ? (
          <section className="section">
            <h2 className="title">Chapters</h2>
            <p className="footnote">Choose a chapter to sort only it; “Exercise” trains only it.</p>
            <ul className="chapter-list">
              {detail.chapters.map((chapter) => {
                const label = chapterLabel(chapter)
                const percent = percentOf(chapter.sortedCount, chapter.wordsCount)
                const action = chapterAction(chapter)

                return (
                  <li key={chapter.id} className={`chapter-row${percent === 100 ? ' done' : ''}`}>
                    <button type="button" className="chapter-main" onClick={() => onSort([chapter.id], label)}>
                      <span className="chapter-text">
                        <span className="chapter-title">{label}</span>
                        <span className="chapter-sub num">
                          {wordsLabel(chapter.wordsCount)} · {chapterSubLine(action)}
                        </span>
                      </span>
                      <span className="chapter-pct num">{percent}%</span>
                      <span className="chevron" aria-hidden="true">
                        ›
                      </span>
                    </button>
                    {action.kind === 'review' ? (
                      <button
                        type="button"
                        className="btn btn-quiet chapter-train"
                        disabled={reviewBusy}
                        aria-label={`Review: ${label}`}
                        onClick={() => void startReview(chapter)}
                      >
                        Review
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="btn btn-quiet chapter-train"
                        disabled={action.kind !== 'exercise'}
                        title={
                          action.kind === 'wait' ? 'Nothing due yet in this chapter'
                          : action.kind === 'none' ? 'No words to learn in this chapter'
                          : undefined
                        }
                        aria-label={`Exercise: ${label}`}
                        onClick={() => onTrain([chapter.id], label)}
                      >
                        Exercise
                      </button>
                    )}
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
          <p className="footnote">This dictionary has no chapters — it can only be sorted as a whole.</p>
        )}

        {detail.topWords.length > 0 && (
          <section className="section">
            <h2 className="title">Most frequent words</h2>
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

/** The second half of a chapter's sub-line: what the row's button is about to offer, or why it can't. */
function chapterSubLine(action: ReturnType<typeof chapterAction>): string {
  switch (action.kind) {
    case 'exercise':
      return `${formatInt(action.learnable)} to learn`
    case 'review':
      return `${formatInt(action.due)} to review`
    case 'wait':
      return action.nextDueAt
        ? `${formatInt(action.inProgress)} in progress · next review ${formatDue(action.nextDueAt, false, new Date())}`
        : `${formatInt(action.inProgress)} in progress`
    case 'none':
      return '0 to learn'
  }
}
