import { formatInt } from '../lib/format'
import './BoxHistogram.css'

interface Props {
  /** Words in progress per Leitner box; index 0 = box 1, learned words are not in here. */
  boxCounts: number[]
}

/** A column per box, the tallest column filling the height — shape over absolute scale. */
export function BoxHistogram({ boxCounts }: Props) {
  const max = Math.max(0, ...boxCounts)
  const summary = boxCounts.map((n, i) => `box ${i + 1} — ${formatInt(n)}`).join(', ')

  return (
    <div className="boxes" role="img" aria-label={`Words by box: ${summary}`}>
      {boxCounts.map((n, i) => {
        // toFixed(2) keeps the inline style stable across renders; 0/0 would be NaN.
        const height = max > 0 ? `${((n / max) * 100).toFixed(2).replace(/\.?0+$/, '')}%` : '0%'

        return (
          <div key={i} className="boxes-col">
            <span className="boxes-count footnote num">{formatInt(n)}</span>
            <span className="boxes-track">
              <span className={`boxes-bar boxes-box${i + 1}`} style={{ height }} />
            </span>
            <span className="boxes-label caption">Box {i + 1}</span>
          </div>
        )
      })}
    </div>
  )
}
