import { formatProgress, percentOf } from '../lib/format'
import './ProgressBar.css'

interface Props {
  sorted: number
  total: number
  /** Підпис «X з Y» + відсоток під шкалою. У сайдбарі й панелі сортування його вимикають. */
  showLabel?: boolean
}

export function ProgressBar({ sorted, total, showLabel = true }: Props) {
  const percent = percentOf(sorted, total)

  return (
    <span className="progress" aria-hidden={showLabel ? undefined : true}>
      <div
        className="progress-track"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
        aria-label={`${percent}%, ${formatProgress(sorted, total)}`}
      >
        <div className="progress-fill" style={{ width: `${percent}%` }} />
      </div>

      {showLabel && (
        <span className="progress-label num">
          <span>{formatProgress(sorted, total)}</span>
          <span>{percent}%</span>
        </span>
      )}
    </span>
  )
}
