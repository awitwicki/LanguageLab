import { useEffect, useState, type ReactNode } from 'react'
import {
  api,
  type ChapterView,
  type RecentActivity,
  type RecentExercise,
  type RecentSorting,
  type StarredChapter,
  type TrainingStarted,
  type TrainingStats,
} from '../api/client'
import { BoxHistogram } from '../components/BoxHistogram'
import { ChapterRow } from '../components/ChapterRow'
import { ScopeRow } from '../components/ScopeRow'
import { formatInt, formatProgress, percentOf, wordsLabel } from '../lib/format'
import { WHOLE_BOOK, chapterLabel, readingProgressLabel, scopeLabel } from '../lib/labels'
import type { BookStore } from '../reader/bookStore'
import { useContinueReading, type ContinueReading } from '../reader/useContinueReading'
import './HomeScreen.css'

interface Props {
  hasDictionaries: boolean
  onImport: () => void
  /** The review across every book, from the "Due today" tile. */
  onReview: (started: TrainingStarted) => void
  /** chapterIds null — the whole book, as the recent-sorting list can ask for. */
  onSort: (dictionaryId: number, chapterIds: number[] | null, scopeTitle: string) => void
  onTrain: (dictionaryId: number, chapterIds: number[] | null, scopeTitle: string) => void
  /** A review of one starred chapter; scopeTitle is the chapter's label. */
  onChapterReview: (started: TrainingStarted, dictionaryId: number, scopeTitle: string) => void
  /** The device's books, for the "Continue" row. null while the store is still opening. */
  bookStore: BookStore | null
  onOpenBook: (hash: string) => void
  /**
   * The reading library, asked to continue the book with that file hash — where the last-read
   * book goes when its file is on another device and only the user can hand it over.
   */
  onOpenLibrary: (hash: string) => void
}

interface StarredBook {
  dictionaryId: number
  dictionaryName: string
  chapters: ChapterView[]
}

/// The list of dictionaries lives in the sidebar, so the home screen is an empty state:
/// either a hint to pick a dictionary, or an invitation to import the first book.
/// Once the user has sorted anything, their shelf totals sit on top of it; once they have
/// trained anything, their global Leitner standing joins them. Below those comes everything
/// there is to pick up again — the book being read, the last exercises, the sorting left
/// unfinished — and then the chapters they starred.
export function HomeScreen({
  hasDictionaries,
  onImport,
  onReview,
  onSort,
  onTrain,
  onChapterReview,
  bookStore,
  onOpenBook,
  onOpenLibrary,
}: Props) {
  const reading = useContinueReading(bookStore)

  const [stats, setStats] = useState<TrainingStats | null>(null)
  const [starred, setStarred] = useState<StarredChapter[] | null>(null)
  const [recent, setRecent] = useState<RecentActivity | null>(null)
  const [recentNotice, setRecentNotice] = useState<string | null>(null)
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

    api
      .recentActivity()
      .then((activity) => {
        if (!cancelled) setRecent(activity)
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

  // "Repeat" reopens the scope, it does not replay the old questions. A batch lands on its
  // start screen, where the preview shows what the next words would be; a review has to ask
  // the server, because what is due has moved on since.
  const repeat = async (exercise: RecentExercise) => {
    const chapterIds = exercise.chapter ? [exercise.chapter.id] : null
    const scopeTitle = exercise.chapter ? chapterLabel(exercise.chapter) : WHOLE_BOOK

    if (exercise.mode === 'newBatch') {
      // A batch always had a book — only a review can span every dictionary.
      if (exercise.dictionaryId !== null) {
        onTrain(exercise.dictionaryId, chapterIds, scopeTitle)
      }

      return
    }

    setReviewBusy(true)
    setRecentNotice(null)

    try {
      const started =
        exercise.dictionaryId === null
          ? await api.startReview()
          : await api.startReview({ dictionaryId: exercise.dictionaryId, chapterIds })

      if (!started) {
        setRecentNotice('Nothing to review today.')
        return
      }

      if (exercise.dictionaryId === null) {
        onReview(started)
      } else {
        onChapterReview(started, exercise.dictionaryId, scopeTitle)
      }
    } catch (e) {
      setRecentNotice(String(e))
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

      {/* Above the starred list on purpose: a star is a shortcut the user can always find
          again, while these rows fade as soon as the work behind them is done. */}
      {(reading.length > 0 || (recent !== null && (recent.exercises.length > 0 || recent.sorting.length > 0))) && (
        <section className="recent-activity">
          <h2 className="title">Pick up where you left off</h2>

          {/* First: one tap back into the book, the cheapest thing on the screen to resume.
              A book whose file is on another device can only go to the library, which asks
              for it — so the newest book that does open here comes with it. */}
          {reading.length > 0 && (
            <div className="recent-group">
              <h3 className="headline">Reading</h3>
              <ul className="scope-list">
                {reading.map((row) => (
                  <ScopeRow
                    key={row.book.fileHash}
                    title={row.book.title}
                    sub={readingSub(row)}
                    action={row.onDevice ? 'Continue' : 'Open the file'}
                    onAction={() => (row.onDevice ? onOpenBook(row.book.fileHash) : onOpenLibrary(row.book.fileHash))}
                  />
                ))}
              </ul>
            </div>
          )}

          {recent && recent.exercises.length > 0 && (
            <div className="recent-group">
              <h3 className="headline">Exercises</h3>
              <ul className="scope-list">
                {recent.exercises.map((exercise) => (
                  <ScopeRow
                    key={exercise.trainingId}
                    title={scopeLabel(exercise.dictionaryName, exercise.chapter)}
                    sub={exerciseSub(exercise)}
                    action="Repeat"
                    busy={reviewBusy}
                    onAction={() => void repeat(exercise)}
                  />
                ))}
              </ul>
            </div>
          )}

          {recent && recent.sorting.length > 0 && (
            <div className="recent-group">
              <h3 className="headline">Sorting</h3>
              <ul className="scope-list">
                {recent.sorting.map((item) => (
                  <ScopeRow
                    key={`${item.dictionaryId}:${item.chapter?.id ?? 'book'}`}
                    title={scopeLabel(item.dictionaryName, item.chapter)}
                    sub={sortingSub(item)}
                    action="Sort"
                    onAction={() =>
                      onSort(
                        item.dictionaryId,
                        item.chapter ? [item.chapter.id] : null,
                        item.chapter ? chapterLabel(item.chapter) : WHOLE_BOOK,
                      )
                    }
                  />
                ))}
              </ul>
            </div>
          )}

          {recentNotice && <p className="footnote recent-notice">{recentNotice}</p>}
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
          Your dictionaries are <span className="hint-desktop">in the sidebar</span>
          <span className="hint-phone">in the picker above</span>. Open one to see its chapters and start sorting.
        </p>
      ) : (
        <>
          <p className="welcome-hint">
            Nothing here yet. Import an .fb2 or .epub book — its words are split by chapter, ready to be
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

/// Where the reader stopped, and — when the file is elsewhere — why the row asks for it
/// instead of opening the book.
function readingSub(reading: ContinueReading): string {
  const place = readingProgressLabel(reading.book)

  return reading.onDevice ? place : `${place} · not on this device`
}

/** What a repeated exercise was: which trainer, and how it went. */
function exerciseSub(exercise: RecentExercise): string {
  const mode = exercise.mode === 'review' ? 'Review' : 'Learn'

  // A session whose every question went out through the "Know" button has nothing to score.
  return exercise.total > 0 ? `${mode} · ${formatProgress(exercise.correct, exercise.total)} correct` : mode
}

/** How far the scope got, and what is still waiting there. */
function sortingSub(item: RecentSorting): string {
  const left = item.total - item.sorted

  return `${percentOf(item.sorted, item.total)}% sorted · ${wordsLabel(left)} left`
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
