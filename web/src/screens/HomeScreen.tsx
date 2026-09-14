import { useEffect, useState, type ReactNode } from 'react'
import { api, type ChapterView, type StarredChapter, type TrainingStarted, type TrainingStats } from '../api/client'
import { BoxHistogram } from '../components/BoxHistogram'
import { ChapterRow } from '../components/ChapterRow'
import { formatInt } from '../lib/format'
import { chapterLabel } from '../lib/labels'
import './HomeScreen.css'

interface Props {
  hasDictionaries: boolean
  onImport: () => void
  /** The review across every book, from the "Due today" tile. */
  onReview: (started: TrainingStarted) => void
  onSort: (dictionaryId: number, chapterIds: number[], scopeTitle: string) => void
  onTrain: (dictionaryId: number, chapterIds: number[], scopeTitle: string) => void
  /** A review of one starred chapter; scopeTitle is the chapter's label. */
  onChapterReview: (started: TrainingStarted, dictionaryId: number, scopeTitle: string) => void
}

interface StarredBook {
  dictionaryId: number
  dictionaryName: string
  chapters: ChapterView[]
}

/// The list of dictionaries lives in the sidebar, so the home screen is an empty state:
/// either a hint to pick a dictionary, or an invitation to import the first book.
/// Once the user has sorted anything, their shelf totals sit on top of it; once they have
/// trained anything, their global Leitner standing joins them; once they have starred a
/// chapter, the starred list sits between the two.
export function HomeScreen({ hasDictionaries, onImport, onReview, onSort, onTrain, onChapterReview }: Props) {
  const [stats, setStats] = useState<TrainingStats | null>(null)
  const [starred, setStarred] = useState<StarredChapter[] | null>(null)
  const [reviewBusy, setReviewBusy] = useState(false)
  const [reviewNotice, setReviewNotice] = useState<string | null>(null)
  const [starBusyId, setStarBusyId] = useState<number | null>(null)
  const [starredNotice, setStarredNotice] = useState<{ text: string; isError: boolean } | null>(null)

  // Best-effort, like the sidebar's verbs percent: a failed request just leaves the
  // block out rather than turning the home screen into an error page.
  useEffect(() => {
    let cancelled = false

    api
      .trainingStats()
      .then((s) => {
        if (!cancelled) setStats(s)
      })
      .catch(() => {})

    api
      .getStarredChapters()
      .then((list) => {
        if (!cancelled) setStarred(list)
      })
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [])

  // The only place to review across every book: a dictionary page reviews its own share.
  const startReview = async () => {
    setReviewBusy(true)
    setReviewNotice(null)

    try {
      const started = await api.startReview()

      // 204: the due words were closed by another session since the stats loaded.
      if (!started) {
        setReviewNotice('Nothing to review today.')
        return
      }

      onReview(started)
    } catch (e) {
      setReviewNotice(String(e))
    } finally {
      setReviewBusy(false)
    }
  }

  // Same race as the tile's review: the chapter's due words may be gone by the click.
  const startChapterReview = async (item: StarredChapter) => {
    setReviewBusy(true)
    setStarredNotice(null)

    try {
      const started = await api.startReview({ dictionaryId: item.dictionaryId, chapterIds: [item.chapter.id] })

      if (!started) {
        setStarredNotice({ text: 'Nothing to review today.', isError: false })
        return
      }

      onChapterReview(started, item.dictionaryId, chapterLabel(item.chapter))
    } catch (e) {
      setStarredNotice({ text: String(e), isError: true })
    } finally {
      setReviewBusy(false)
    }
  }

  // Everything here is starred already, so the toggle only ever removes; the row goes
  // once the server agrees — there is nothing to flip back optimistically.
  const unstar = async (item: StarredChapter) => {
    setStarBusyId(item.chapter.id)
    setStarredNotice(null)

    try {
      await api.unstarChapter(item.chapter.id)
      setStarred((list) => list?.filter((s) => s.chapter.id !== item.chapter.id) ?? list)
    } catch (e) {
      setStarredNotice({ text: String(e), isError: true })
    } finally {
      setStarBusyId(null)
    }
  }

  const inProgress = stats ? stats.boxCounts.reduce((sum, n) => sum + n, 0) : 0
  const hasSorted = stats !== null && stats.known + stats.unknown + stats.excluded > 0
  const hasTrained = stats !== null && inProgress + stats.learned > 0
  const books = groupByBook(starred ?? [])

  return (
    <section className="welcome">
      {(hasSorted || hasTrained) && stats && (
        <section className="learning-stats">
          <h2 className="title">Your learning</h2>

          <dl className="stat-tiles">
            <StatTile label="Known" value={stats.known} />
            <StatTile label="To learn" value={stats.unknown} />
            <StatTile label="Excluded" value={stats.excluded} />
          </dl>

          {hasTrained && (
            <>
              <dl className="stat-tiles">
                <StatTile label="In progress" value={inProgress} />
                <StatTile label="Learned" value={stats.learned} />
                <StatTile
                  label="Due today"
                  value={stats.due}
                  action={
                    stats.due > 0 && (
                      <button type="button" className="btn btn-primary" disabled={reviewBusy} onClick={() => void startReview()}>
                        Review
                      </button>
                    )
                  }
                />
              </dl>

              {reviewNotice && <p className="footnote">{reviewNotice}</p>}

              <BoxHistogram boxCounts={stats.boxCounts} />

              <p className="footnote">Each correct answer moves a word up a box; box 5 graduates to learned.</p>
            </>
          )}
        </section>
      )}

      {books.length > 0 && (
        <section className="starred-chapters">
          <h2 className="title">Starred chapters</h2>

          {books.map((book) => (
            <div key={book.dictionaryId} className="starred-book">
              <h3 className="headline">{book.dictionaryName}</h3>
              <ul className="chapter-list">
                {book.chapters.map((chapter) => {
                  const item = { dictionaryId: book.dictionaryId, dictionaryName: book.dictionaryName, chapter }

                  return (
                    <ChapterRow
                      key={chapter.id}
                      chapter={chapter}
                      compact
                      reviewBusy={reviewBusy}
                      starBusy={starBusyId === chapter.id}
                      onSort={() => onSort(book.dictionaryId, [chapter.id], chapterLabel(chapter))}
                      onTrain={() => onTrain(book.dictionaryId, [chapter.id], chapterLabel(chapter))}
                      onReview={() => void startChapterReview(item)}
                      onToggleStar={() => void unstar(item)}
                    />
                  )
                })}
              </ul>
            </div>
          ))}

          {starredNotice && (
            <p className={`footnote starred-notice${starredNotice.isError ? ' error' : ''}`}>{starredNotice.text}</p>
          )}
        </section>
      )}

      <h1 className="large-title">{hasDictionaries ? 'Pick a dictionary' : 'Start with a book'}</h1>

      {hasDictionaries ? (
        <p className="welcome-hint">
          Your dictionaries are in the sidebar. Open one to see its stats and start sorting.
        </p>
      ) : (
        <>
          <p className="welcome-hint">
            Nothing here yet. Import an .fb2 book — its words are split by chapter, ready to be
            sorted into “know” and “don’t know”.
          </p>
          <button type="button" className="btn btn-primary btn-lg" onClick={onImport}>
            Import a book
          </button>
        </>
      )}
    </section>
  )
}

/** The API already orders by book name then chapter order; this only folds consecutive rows of one book together. */
function groupByBook(starred: StarredChapter[]): StarredBook[] {
  const books: StarredBook[] = []

  for (const item of starred) {
    const last = books[books.length - 1]

    if (last && last.dictionaryId === item.dictionaryId) {
      last.chapters.push(item.chapter)
    } else {
      books.push({ dictionaryId: item.dictionaryId, dictionaryName: item.dictionaryName, chapters: [item.chapter] })
    }
  }

  return books
}

function StatTile({ label, value, action }: { label: string; value: number; action?: ReactNode }) {
  return (
    <div className="stat-tile">
      <dt className="stat-label footnote">{label}</dt>
      <dd className="stat-row">
        <span className="stat-value title num">{formatInt(value)}</span>
        {action}
      </dd>
    </div>
  )
}
