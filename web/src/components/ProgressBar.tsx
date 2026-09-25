import { formatProgress, percentOf } from '../lib/format'
import './ProgressBar.css'

interface Props {
  sorted: number
  total: number
  /** The "X of Y" caption + percentage under the bar. The sidebar and the sorting panel turn it off. */
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
