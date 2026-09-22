import { useEffect, useState } from 'react'
import { api, type SessionStarted, type VerbsProgress } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import './VerbsScreen.css'

interface Props {
  onOpenGroup: (group: number) => void
  onStartSession: (started: SessionStarted) => void
}

/// Home screen of the irregular-verbs program: overall progress, one tile per
/// group, and the two cross-group sessions (errors only, mixed).
export function VerbsScreen({ onOpenGroup, onStartSession }: Props) {
  const [progress, setProgress] = useState<VerbsProgress | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api
      .getVerbsProgress()
      .then(setProgress)
      .catch((e) => setError(String(e)))
  }, [])

  const start = async (mode: 'errorsOnly' | 'mixed') => {
    setBusy(true)
    setError(null)

    try {
      const started = await api.startVerbSession({ mode })
      onStartSession(started)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  const continueSession = () => {
    if (!progress?.activeSession) {
      return
    }

    const { id, mode, group, family, total } = progress.activeSession
    onStartSession({ id, mode, group, family, title: '', total })
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!progress) {
    return <p className="footnote">Loading…</p>
  }

  return (
    <>
      <h1 className="large-title">Irregular verbs</h1>
      <p className="verbs-intro">Four groups by pattern — learn them as families, not alphabetically.</p>

      {progress.activeSession && (
        <div className="continue-banner">
          <span>
            Continue where you left off — {formatInt(progress.activeSession.answered)} of{' '}
            {formatInt(progress.activeSession.total)} done
          </span>
          <button type="button" className="btn btn-primary" onClick={continueSession}>
            Continue
          </button>
        </div>
      )}

      <div className="group-tiles">
        {progress.groups.map((group) => (
          <section key={group.group} className="group-tile">
            <p className="group-tile-title">{group.title}</p>
            <ProgressBar sorted={group.learned} total={group.total} showLabel={false} />
            <p className="footnote num group-tile-caption">
              {formatInt(group.learned)} of {formatInt(group.total)} learned
            </p>
            <button type="button" className="btn btn-secondary" onClick={() => onOpenGroup(group.group)}>
              Open
            </button>
          </section>
        ))}
      </div>

      <div className="session-buttons">
        <button
          type="button"
          className="btn btn-secondary errors-only-btn"
          disabled={!progress.errorsAvailable || busy}
          onClick={() => void start('errorsOnly')}
        >
          Errors only
        </button>
        <button
          type="button"
          className="btn btn-secondary mixed-session-btn"
          disabled={!progress.mixedAvailable || busy}
          onClick={() => void start('mixed')}
        >
          Mixed session
        </button>
      </div>
      <p className="footnote session-hint">
        Errors only repeats the verbs you got wrong. Mixed session draws ten tasks from every verb you have started,
        once two families are done.
      </p>

      <div className="verbs-notes footnote">
        <p>
          <strong>read</strong> is spelt the same in all three forms but pronounced differently: /riːd/ for the
          present, /red/ for the past forms.
        </p>
        <p>
          British English uses <strong>got</strong> for the past participle of get; American English uses{' '}
          <strong>gotten</strong>.
        </p>
        <p>
          Each family shares a sound or spelling pattern — learn it as one small group rather than 68 separate words.
        </p>
      </div>
    </>
  )
}
