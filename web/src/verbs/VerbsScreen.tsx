import { useEffect, useState } from 'react'
import { api, type DrillQuery, type VerbsProgress } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import { createOutbox } from './outbox'
import { clearSavedSession, readSavedSession, type SavedSession } from './savedSession'
import { VerbTable } from './VerbTable'
import './VerbsScreen.css'

interface Props {
  userId: number
  /// Carries on with a round a reload interrupted.
  onResume: (saved: SavedSession) => void
  onOpenStage: (group: number) => void
  onStartDrill: (query: DrillQuery, title: string) => void
}

/// The program's front page: four stages by verb type, a free run over everything, and
/// the whole catalog underneath so the learner can see where they stand at a glance. A round a
/// reload interrupted is offered back on top.
export function VerbsScreen({ userId, onResume, onOpenStage, onStartDrill }: Props) {
  const [progress, setProgress] = useState<VerbsProgress | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(() => readSavedSession(userId))

  // The round is dropped, not its answers: whatever it never sent still goes to the server.
  const discard = () => {
    if (saved && saved.pending.length > 0) {
      createOutbox(api.postVerbAnswers, saved.pending)
        .flush()
        .catch(() => {})
    }

    clearSavedSession()
    setSaved(null)
  }

  useEffect(() => {
    api
      .getVerbsProgress()
      .then(setProgress)
      .catch((e) => setError(String(e)))
  }, [])

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!progress) {
    return <p className="footnote">Loading…</p>
  }

  return (
    <>
      <h1 className="large-title">Irregular verbs</h1>
      <p className="verbs-intro">
        Four stages by verb type. A card shows one form — say whether you know the other two.
      </p>

      {saved && (
        <section className="verbs-resume">
          <div>
            <p className="headline verbs-resume-title">Unfinished session</p>
            <p className="footnote num">
              {saved.title} · {formatInt(saved.scope.passed)} of {formatInt(saved.scope.total)} passed
            </p>
          </div>
          <div className="verbs-resume-actions">
            <button type="button" className="btn btn-primary" onClick={() => onResume(saved)}>
              Resume session
            </button>
            <button type="button" className="btn btn-quiet" onClick={discard}>
              Discard
            </button>
          </div>
        </section>
      )}

      <div className="stage-tiles">
        {progress.stages.map((stage) => (
          <section key={stage.group} className="stage-tile">
            <p className="stage-tile-title">{stage.title}</p>
            <ProgressBar sorted={stage.passed} total={stage.total} showLabel={false} />
            <p className="footnote num stage-tile-caption">
              {formatInt(stage.passed)} of {formatInt(stage.total)} passed
            </p>
            <button type="button" className="btn btn-secondary" onClick={() => onOpenStage(stage.group)}>
              Open
            </button>
          </section>
        ))}
      </div>

      <button
        type="button"
        className="btn btn-primary verbs-free"
        onClick={() => onStartDrill({ mode: 'free', scope: 'all' }, 'All verbs')}
      >
        Train all verbs
      </button>

      <h2 className="title verbs-all-title">Every verb</h2>
      {progress.stages.map((stage) => (
        <section key={stage.group} className="verbs-all-stage">
          <h3 className="headline">{stage.title}</h3>
          <VerbTable verbs={progress.verbs.filter((v) => v.group === stage.group)} />
        </section>
      ))}
    </>
  )
}
