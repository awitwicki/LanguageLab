import { useEffect, useState, type ReactNode } from 'react'
import { api, type TrainingStarted, type TrainingStats } from '../api/client'
import { BoxHistogram } from '../components/BoxHistogram'
import { formatInt } from '../lib/format'
import './HomeScreen.css'

interface Props {
  hasDictionaries: boolean
  onImport: () => void
  onReview: (started: TrainingStarted) => void
}

/// Список словників живе в сайдбарі, тож «домівка» — це порожній стан:
/// або підказка обрати словник, або запрошення імпортувати першу книжку.
/// Once the user has trained anything, their global Leitner standing sits on top of it.
export function HomeScreen({ hasDictionaries, onImport, onReview }: Props) {
  const [stats, setStats] = useState<TrainingStats | null>(null)
  const [reviewBusy, setReviewBusy] = useState(false)
  const [reviewNotice, setReviewNotice] = useState<string | null>(null)

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

  const inProgress = stats ? stats.boxCounts.reduce((sum, n) => sum + n, 0) : 0
  const hasActivity = stats !== null && inProgress + stats.learned > 0

  return (
    <section className="welcome">
      {hasActivity && stats && (
        <section className="learning-stats">
          <h2 className="title">Your learning</h2>

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
