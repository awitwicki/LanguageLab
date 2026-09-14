import { formatInt, formatProgress, percentOf } from '../lib/format'
import { ProgressBar } from './ProgressBar'
import './SortingProgress.css'

interface Props {
  /** The dictionary name — small, above the heading. */
  scope: string
  /** What exactly is being sorted or trained: the chapter name or "Whole book". */
  title: string
  sorted: number
  total: number
  /** The caption under the bar. By default "X of Y words, Z left"; the quiz passes "Question N of M". */
  counts?: string
}

export function SortingProgress({ scope, title, sorted, total, counts }: Props) {
  const percent = percentOf(sorted, total)
  const remaining = Math.max(0, total - sorted)

  return (
    <section className="sorting-progress">
      <div className="sorting-progress-head">
        <p className="scope">{scope}</p>
        <h1 className="title">{title}</h1>
      </div>
      <p className="percent num">{percent}%</p>
      <ProgressBar sorted={sorted} total={total} showLabel={false} />
      <p className="counts num" aria-live="polite">
        {counts ?? `${formatProgress(sorted, total)} words, ${formatInt(remaining)} left`}
      </p>
    </section>
  )
}
