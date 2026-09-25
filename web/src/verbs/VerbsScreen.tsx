import { useEffect, useState } from 'react'
import { api, type DrillQuery, type VerbsProgress } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import { VerbTable } from './VerbTable'
import './VerbsScreen.css'

interface Props {
  onOpenStage: (group: number) => void
  onStartDrill: (query: DrillQuery, title: string) => void
}

/// The program's front page: four stages by verb type, a free run over everything, and
/// the whole catalog underneath so the learner can see where they stand at a glance.
export function VerbsScreen({ onOpenStage, onStartDrill }: Props) {
  const [progress, setProgress] = useState<VerbsProgress | null>(null)
  const [error, setError] = useState<string | null>(null)

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
