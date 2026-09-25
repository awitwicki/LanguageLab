import { useEffect, useState } from 'react'
import { api, type DrillQuery, type VerbsProgress } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import { VerbTable } from './VerbTable'
import './VerbStageScreen.css'

interface Props {
  group: number
  onStartDrill: (query: DrillQuery, title: string) => void
  onBack: () => void
}

/// One stage: every verb of that type in a table, and the ways to train them — the
/// five-word window, or a free run over this stage alone or together with the earlier ones.
export function VerbStageScreen({ group, onStartDrill, onBack }: Props) {
  const [progress, setProgress] = useState<VerbsProgress | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api
      .getVerbsProgress()
      .then(setProgress)
      .catch((e) => setError(String(e)))
  }, [group])

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!progress) {
    return <p className="footnote">Loading…</p>
  }

  const stage = progress.stages.find((s) => s.group === group)

  if (!stage) {
    return <p className="error">No such stage.</p>
  }

  return (
    <>
      <button type="button" className="btn btn-quiet stage-back" onClick={onBack}>
        Back
      </button>

      <h1 className="large-title">{stage.title}</h1>
      <ProgressBar sorted={stage.passed} total={stage.total} showLabel={false} />
      <p className="footnote num stage-caption">
        {formatInt(stage.passed)} of {formatInt(stage.total)} passed
      </p>

      <div className="stage-actions">
        <button
          type="button"
          className="btn btn-primary stage-train"
          onClick={() => onStartDrill({ mode: 'batch', group }, stage.title)}
        >
          Train
        </button>
        <button
          type="button"
          className="btn btn-secondary stage-free"
          onClick={() => onStartDrill({ mode: 'free', group, scope: 'stage' }, stage.title)}
        >
          Train freely
        </button>
        {group > 1 && (
          <button
            type="button"
            className="btn btn-secondary stage-cumulative"
            onClick={() => onStartDrill({ mode: 'free', group, scope: 'cumulative' }, `${stage.title} and earlier`)}
          >
            Train with earlier stages
          </button>
        )}
      </div>

      <VerbTable verbs={progress.verbs.filter((v) => v.group === group)} />
    </>
  )
}
