import { useEffect, useState } from 'react'
import { useLearnerLanguage } from '../account/learnerLanguage'
import {
  api,
  type ChapterView,
  type DictionaryDetail,
  type PublicationStatus,
  type TopWord,
  type TrainingStarted,
  type UserRole,
} from '../api/client'
import { ChapterRow } from '../components/ChapterRow'
import { ScopeProgress } from '../components/ScopeProgress'
import { chaptersLabel, formatInt, wordsLabel } from '../lib/format'
import { WHOLE_BOOK, chapterLabel } from '../lib/labels'
import { PUBLICATION_STATUSES, publicationStatusLabel } from '../lib/publicationStatus'
import './DictionaryScreen.css'

interface Props {
  id: number
  role: UserRole
  onSort: (chapterIds: number[] | null, scopeTitle: string) => void
  onTrain: (chapterIds: number[] | null, scopeTitle: string) => void
  /** scopeTitle is the chapter for a chapter review; absent for the global one. */
  onReview: (started: TrainingStarted, scopeTitle?: string) => void
  onDeleted: () => void
}

export function DictionaryScreen({ id, role, onSort, onTrain, onReview, onDeleted }: Props) {
  const learnerLanguage = useLearnerLanguage()
  const [detail, setDetail] = useState<DictionaryDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reviewBusy, setReviewBusy] = useState(false)
  const [reviewNotice, setReviewNotice] = useState<string | null>(null)
  const [visibilityBusy, setVisibilityBusy] = useState(false)
  const [visibilityError, setVisibilityError] = useState<string | null>(null)
  const [publicationBusy, setPublicationBusy] = useState(false)
  const [publicationError, setPublicationError] = useState<string | null>(null)
  const [deleteConfirming, setDeleteConfirming] = useState(false)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [excludingId, setExcludingId] = useState<number | null>(null)
  const [excludeNotice, setExcludeNotice] = useState<string | null>(null)
  const [excludeError, setExcludeError] = useState<string | null>(null)
  const [starBusyId, setStarBusyId] = useState<number | null>(null)
  const [starError, setStarError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    // Reset so the previous dictionary does not flash when switching in the sidebar.
    setDetail(null)
    setError(null)
    setReviewNotice(null)
    setExcludeNotice(null)
    setExcludeError(null)
    setStarError(null)
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

  // The SPA's first polling UI: while a background translation job is running for this
  // dictionary, refresh every 4s so "Translating… N of M" moves and clears on its own. Polls
  // getTranslationProgress, not getDictionary — that one also re-enqueues on every call, which
  // turned a tick into a fresh trigger and could re-arm a job forever (final review, finding 1).
  // Merges only translation into the existing detail, so a response in flight cannot clobber an
  // optimistic change (e.g. a star toggle) made between two ticks.
  const translating = detail?.translation != null
  useEffect(() => {
    if (!translating) return
    let cancelled = false
    const interval = setInterval(() => {
      api
        .getTranslationProgress(id)
        .then((translation) => {
          if (!cancelled) setDetail((prev) => (prev ? { ...prev, translation } : prev))
        })
        .catch(() => {
          // A transient blip skips this tick; the next one tries again.
        })
    }, 4000)
    return () => {
      cancelled = true
      clearInterval(interval)
    }
  }, [id, translating])

  // One path for both the header's book-wide review and a chapter's own: the scope is the
  // only difference, and a chapter's 204 is the same race as the book's. The review across
  // every book starts from the home screen instead.
  const startReview = async (chapter?: ChapterView) => {
    setReviewBusy(true)
    setReviewNotice(null)

    try {
      const started = await api.startReview({ dictionaryId: id, chapterIds: chapter ? [chapter.id] : null })

      // 204: another session may have closed the due words between loading the screen and the click.
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

  const changeStatus = async (status: PublicationStatus) => {
    if (!detail) return
    const previous = detail.status
    setVisibilityBusy(true)
    setVisibilityError(null)
    setDetail({ ...detail, status })

    try {
      await api.setDictionaryStatus(id, status)
    } catch (e) {
      setDetail((d) => (d ? { ...d, status: previous } : d))
      setVisibilityError(String(e))
    } finally {
      setVisibilityBusy(false)
    }
  }

  // The owner's own path, separate from the admin's direct status picker above: offers a
  // private (or rejected) dictionary for review, optimistically, and reverts on failure.
  const submitForReview = async () => {
    if (!detail) return
    const previous = detail.status
    setPublicationBusy(true)
    setPublicationError(null)
    setDetail({ ...detail, status: 'pending' })

    try {
      await api.requestPublication(id)
    } catch (e) {
      setDetail((d) => (d ? { ...d, status: previous } : d))
      setPublicationError(e instanceof Error ? e.message : String(e))
    } finally {
      setPublicationBusy(false)
    }
  }

  const withdrawOffer = async () => {
    if (!detail) return
    const previous = detail.status
    setPublicationBusy(true)
    setPublicationError(null)
    setDetail({ ...detail, status: 'private' })

    try {
      await api.withdrawPublication(id)
    } catch (e) {
      setDetail((d) => (d ? { ...d, status: previous } : d))
      setPublicationError(e instanceof Error ? e.message : String(e))
    } finally {
      setPublicationBusy(false)
    }
  }

  // Irreversible, so one click only arms the second — same pattern as AccountMenu's own-account delete.
  const removeDictionary = async () => {
    if (!deleteConfirming) {
      setDeleteConfirming(true)
      return
    }

    setDeleteBusy(true)
    setDeleteError(null)

    try {
      await api.deleteDictionary(id)
      onDeleted()
    } catch (e) {
      setDeleteConfirming(false)
      setDeleteError(e instanceof Error ? e.message : String(e))
    } finally {
      setDeleteBusy(false)
    }
  }

  // A word excluded here also affects sortedCount/learnableCount/etc, so a full refetch
  // (rather than a local splice) both backfills the list and keeps the rest of the screen right.
  const excludeWord = async (word: TopWord) => {
    setExcludingId(word.wordPairId)
    setExcludeError(null)
    setExcludeNotice(null)

    try {
      await api.mark(word.wordPairId, 'excluded')
      setDetail(await api.getDictionary(id))
      setExcludeNotice(word.word)
    } catch (e) {
      setExcludeError(e instanceof Error ? e.message : String(e))
    } finally {
      setExcludingId(null)
    }
  }

  const undoExclude = async () => {
    setExcludeError(null)

    try {
      await api.undo()
      setDetail(await api.getDictionary(id))
      setExcludeNotice(null)
    } catch (e) {
      setExcludeError(e instanceof Error ? e.message : String(e))
    }
  }

  // Optimistic, like the visibility toggle: nothing else on the screen depends on a star,
  // so a local flip is enough and the request only has to be undone on failure.
  const toggleStar = async (chapter: ChapterView) => {
    const next = !chapter.isStarred
    const flip = (d: DictionaryDetail | null, value: boolean) =>
      d ? { ...d, chapters: d.chapters.map((c) => (c.id === chapter.id ? { ...c, isStarred: value } : c)) } : d

    setStarBusyId(chapter.id)
    setStarError(null)
    setDetail((d) => flip(d, next))

    try {
      if (next) await api.starChapter(chapter.id)
      else await api.unstarChapter(chapter.id)
    } catch (e) {
      setDetail((d) => flip(d, !next))
      setStarError(e instanceof Error ? e.message : String(e))
    } finally {
      setStarBusyId(null)
    }
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!detail) {
    return <p className="footnote">Loading…</p>
  }

  // The frequency bar is relative to the leader, so the top reads as a histogram, not a table.
  const maxFrequency = detail.topWords[0]?.frequency ?? 1
  // The mini-list at the top: the same rows, only the starred ones, still in book order.
  const starred = detail.chapters.filter((c) => c.isStarred)
  const unsortedLeft = detail.sortedCount < detail.wordsCount

  return (
    <>
      <header className="dict-header">
        <h1 className="large-title">{detail.name}</h1>
        <p className="dict-meta num">
          {wordsLabel(detail.wordsCount)}
          {detail.chapters.length > 0 && `, ${chaptersLabel(detail.chapters.length)}`}
        </p>
        <ScopeProgress sorting={{ sorted: detail.sortedCount, total: detail.wordsCount }} learning={detail.learning} />
      </header>

      {/* Sorting leads while there is anything left to sort; once the book is through, learning takes the lead. */}
      <div className="dict-actions">
        {unsortedLeft && (
          <button type="button" className="btn btn-primary" onClick={() => onSort(null, WHOLE_BOOK)}>
            Sort the whole book
          </button>
        )}
        <button
          type="button"
          className={`btn ${unsortedLeft ? 'btn-secondary' : 'btn-primary'}`}
          disabled={detail.learnableCount === 0}
          onClick={() => onTrain(null, WHOLE_BOOK)}
        >
          Learn new words
        </button>
        {detail.dueCount > 0 && (
          <button type="button" className="btn btn-secondary" disabled={reviewBusy} onClick={() => void startReview()}>
            Review ({formatInt(detail.dueCount)})
          </button>
        )}
      </div>

      {detail.translation ? (
        <p className="footnote dict-actions-hint">
          Translating… {formatInt(detail.translation.done)} of {formatInt(detail.translation.total)}
        </p>
      ) : (
        detail.learnableCount === 0 && (
          <p className="footnote dict-actions-hint">
            {learnerLanguage === 'uk'
              ? 'No words to learn yet: mark words as “don’t know” while sorting.'
              : 'No words translated into your language yet — look words up while reading, or add translations in My words.'}
          </p>
        )
      )}

      {reviewNotice && <p className="footnote dict-actions-hint">{reviewNotice}</p>}

      {/* Chapters on the left (first on a narrow screen), the most frequent words on the right. */}
      <div className="dict-columns">
        {detail.chapters.length > 0 ? (
          <div className="dict-chapters">
            {starred.length > 0 && (
              <section className="section">
                <h2 className="title">Starred</h2>
                <ul className="chapter-list">
                  {starred.map((chapter) => (
                    <ChapterRow
                      key={chapter.id}
                      chapter={chapter}
                      compact
                      reviewBusy={reviewBusy}
                      starBusy={starBusyId === chapter.id}
                      onSort={() => onSort([chapter.id], chapterLabel(chapter))}
                      onTrain={() => onTrain([chapter.id], chapterLabel(chapter))}
                      onReview={() => void startReview(chapter)}
                      onToggleStar={() => void toggleStar(chapter)}
                    />
                  ))}
                </ul>
              </section>
            )}

            <section className="section">
              <h2 className="title">Chapters</h2>
              <p className="footnote">A row’s Sort and Learn act on that chapter alone; the buttons above take the whole book.</p>
              {starError && <p className="footnote error">{starError}</p>}
              <ul className="chapter-list">
                {detail.chapters.map((chapter) => (
                  <ChapterRow
                    key={chapter.id}
                    chapter={chapter}
                    reviewBusy={reviewBusy}
                    starBusy={starBusyId === chapter.id}
                    onSort={() => onSort([chapter.id], chapterLabel(chapter))}
                    onTrain={() => onTrain([chapter.id], chapterLabel(chapter))}
                    onReview={() => void startReview(chapter)}
                    onToggleStar={() => void toggleStar(chapter)}
                  />
                ))}
              </ul>
            </section>
          </div>
        ) : (
          <p className="footnote">This dictionary has no chapters — it can only be sorted as a whole.</p>
        )}

        {detail.topWords.length > 0 && (
          <section className="section">
            <h2 className="title">Most frequent words</h2>
            {excludeNotice && (
              <p className="footnote top-words-notice">
                Excluded "{excludeNotice}".{' '}
                <button type="button" className="btn btn-quiet" onClick={() => void undoExclude()}>
                  Undo
                </button>
              </p>
            )}
            {excludeError && <p className="footnote error">{excludeError}</p>}
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
                  <button
                    type="button"
                    className="btn btn-quiet exclude-word"
                    disabled={excludingId === item.wordPairId}
                    aria-label={`Exclude "${item.word}"`}
                    title="Not a real word (a name, a place) — exclude it from this list and from exercises"
                    onClick={() => void excludeWord(item)}
                  >
                    ×
                  </button>
                </li>
              ))}
            </ol>
          </section>
        )}
      </div>

      {/* The owner's own publication request, separate from the admin's direct status picker
          below: a non-admin viewer of a private/pending/rejected dictionary is necessarily its
          owner (those statuses 404 for anyone else — DictionaryAccessService.Visible). */}
      {role !== 'admin' && (
        <section className="section dict-sharing">
          <h2 className="title">Sharing</h2>
          {detail.status === 'private' && (
            <>
              <p className="footnote">Only you can see this dictionary.</p>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={publicationBusy}
                onClick={() => void submitForReview()}
              >
                Submit for review
              </button>
            </>
          )}
          {detail.status === 'pending' && (
            <>
              <p className="footnote">Waiting for review.</p>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={publicationBusy}
                onClick={() => void withdrawOffer()}
              >
                Withdraw
              </button>
            </>
          )}
          {detail.status === 'published' && (
            <p className="footnote">Everyone who signs in can see this dictionary.</p>
          )}
          {detail.status === 'rejected' && (
            <p className="footnote">An administrator did not approve this dictionary.</p>
          )}
          {publicationError && <p className="footnote error">{publicationError}</p>}
        </section>
      )}

      {/* Admin-only housekeeping lives after the content: it is rare, and one of its buttons is irreversible. */}
      {role === 'admin' && (
        <section className="section dict-admin">
          <h2 className="title">Manage</h2>
          <label className="dict-visibility">
            Status
            <select
              value={detail.status}
              disabled={visibilityBusy}
              onChange={(e) => void changeStatus(e.target.value as PublicationStatus)}
            >
              {PUBLICATION_STATUSES.map((status) => (
                <option key={status} value={status}>
                  {publicationStatusLabel(status)}
                </option>
              ))}
            </select>
            <span className="caption">published dictionaries are visible to everyone who signs in</span>
          </label>
          {visibilityError && <p className="footnote error">{visibilityError}</p>}
          <div className="dict-danger-zone">
            <button
              type="button"
              className="btn btn-quiet delete-dictionary"
              disabled={deleteBusy}
              onClick={() => void removeDictionary()}
            >
              {deleteConfirming ? 'Confirm deletion' : 'Delete dictionary'}
            </button>
            {deleteConfirming && (
              <p className="footnote delete-warning">
                This deletes the dictionary and its chapters for everyone. Cannot be undone.
              </p>
            )}
            {deleteError && <p className="footnote error">{deleteError}</p>}
          </div>
        </section>
      )}
    </>
  )
}
