import { useEffect, useState } from 'react'
import { api, type FamilyView, type SessionStarted, type VerbsProgress } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import { stateLabel } from './format'
import './VerbGroupScreen.css'

interface Props {
  group: number
  onBack: () => void
  onStartSession: (started: SessionStarted) => void
}

function actionLabel(family: FamilyView): string {
  if (family.status === 'done') {
    return 'Practise again'
  }

  const started = family.verbs.some((v) => v.state !== 'new')
  return started ? 'Continue' : 'Start'
}

/// One group's families laid out as a path, every one open to train — done ones
/// offer a refresher. The only table outside a session lives here, behind a
/// per-family toggle.
export function VerbGroupScreen({ group, onBack, onStartSession }: Props) {
  const [progress, setProgress] = useState<VerbsProgress | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  useEffect(() => {
    api
      .getVerbsProgress()
      .then(setProgress)
      .catch((e) => setError(String(e)))
  }, [])

  const toggle = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const start = async (family: FamilyView) => {
    setBusy(family.key)
    setError(null)

    try {
      const started = await api.startVerbSession({ mode: 'learn', family: family.key })
      onStartSession(started)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(null)
    }
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!progress) {
    return <p className="footnote">Loading…</p>
  }

  const groupView = progress.groups.find((g) => g.group === group)

  if (!groupView) {
    return <p className="error">Unknown group.</p>
  }

  return (
    <>
      <button type="button" className="btn btn-quiet" onClick={onBack}>
        ‹ Irregular verbs
      </button>
      <h1 className="large-title">{groupView.title}</h1>

      <div className="group-path">
        {groupView.families.map((family) => {
          return (
            <section key={family.key} className="family-node" data-status={family.status}>
              <div className="family-node-head">
                <p className="family-node-title">{family.title}</p>
                <div className="family-node-actions">
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={busy === family.key}
                    onClick={() => void start(family)}
                  >
                    {actionLabel(family)}
                  </button>
                  <button
                    type="button"
                    className="btn btn-quiet family-verbs-toggle"
                    onClick={() => toggle(family.key)}
                    aria-expanded={expanded.has(family.key)}
                  >
                    {expanded.has(family.key) ? 'Hide verbs' : 'Show verbs'}
                  </button>
                </div>
              </div>

              <ProgressBar sorted={family.learned} total={family.total} showLabel={false} />
              <p className="footnote num">
                {formatInt(family.learned)} of {formatInt(family.total)} learned
              </p>

              {expanded.has(family.key) && (
                <table className="family-table">
                  <thead>
                    <tr>
                      <th>V1</th>
                      <th>V2</th>
                      <th>V3</th>
                      <th>Translation</th>
                      <th>State</th>
                    </tr>
                  </thead>
                  <tbody>
                    {family.verbs.map((verb) => (
                      <tr key={verb.v1}>
                        <td>{verb.v1}</td>
                        <td>{verb.v2}</td>
                        <td>{verb.v3}</td>
                        <td>{verb.translation}</td>
                        <td>{stateLabel(verb.state)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
          )
        })}
      </div>
    </>
  )
}
