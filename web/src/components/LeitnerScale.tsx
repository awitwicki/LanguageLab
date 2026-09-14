import type { LearningProgress } from '../api/client'
import { formatInt } from '../lib/format'
import { learningPercent, learningSegments } from '../lib/learning'
import './LeitnerScale.css'

/** The percent is a weighted score, not a share of words — this line says so wherever the scale stands alone. */
const CAPTION =
  'How far the words marked “don’t know” have climbed the Leitner boxes: box 1 counts one fifth, learned counts in full.'

interface Props {
  progress: LearningProgress
  /** compact — bar + percentage on one line (chapter row, header); large — percentage on top, legend below (start screen). */
  size?: 'compact' | 'large'
  /** A footnote under the scale explaining the percent. Always on for large; the book header turns it on, chapter rows under it don't repeat it. */
  caption?: boolean
}

export function LeitnerScale({ progress, size = 'compact', caption = size === 'large' }: Props) {
  // No "don't know" words — nothing to show; the caller reserves no space.
  if (progress.total <= 0) {
    return null
  }

  const percent = learningPercent(progress)
  const segments = learningSegments(progress)
  const summary = segments.map((s) => `${s.label} — ${formatInt(s.count)}`).join(', ')

  return (
    <div className={`leitner leitner-${size}`} role="img" aria-label={`Learned ${percent}%: ${summary}`}>
      {size === 'large' && <p className="leitner-percent headline num">{percent}% learned</p>}

      <div className="leitner-track">
        {segments
          .filter((s) => s.count > 0)
          .map((s) => (
            <span key={s.key} className={`leitner-seg leitner-${s.key}`} style={{ flexBasis: `${s.share * 100}%` }} />
          ))}
      </div>

      {size === 'compact' && <span className="leitner-percent footnote num">{percent}%</span>}

      {size === 'large' && (
        <ul className="leitner-legend footnote num">
          {segments.map((s) => (
            <li key={s.key} className={s.count === 0 ? 'is-empty' : undefined}>
              <span className={`leitner-dot leitner-${s.key}`} />
              {s.label} {formatInt(s.count)}
            </li>
          ))}
        </ul>
      )}

      {caption && <p className="leitner-caption footnote">{CAPTION}</p>}
    </div>
  )
}
