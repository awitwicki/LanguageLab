import { formatInt, formatProgress, percentOf } from '../lib/format'
import { ProgressBar } from './ProgressBar'
import './SortingProgress.css'

interface Props {
  /** Назва словника — дрібно над заголовком. */
  scope: string
  /** Що саме сортуємо: назва глави або «Уся книжка». */
  title: string
  sorted: number
  total: number
}

export function SortingProgress({ scope, title, sorted, total }: Props) {
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
        {formatProgress(sorted, total)} слів, залишилось {formatInt(remaining)}
      </p>
    </section>
  )
}
