import type { LearningProgress } from '../api/client'
import { formatInt } from '../lib/format'
import { learningPercent, learningSegments } from '../lib/learning'
import './LeitnerScale.css'

interface Props {
  progress: LearningProgress
  /** compact — смуга + відсоток в один рядок (рядок глави, хедер); large — відсоток зверху, легенда знизу (екран старту). */
  size?: 'compact' | 'large'
}

export function LeitnerScale({ progress, size = 'compact' }: Props) {
  // Немає слів «не знаю» — нема що показувати; викликач не резервує місця.
  if (progress.total <= 0) {
    return null
  }

  const percent = learningPercent(progress)
  const segments = learningSegments(progress)
  const summary = segments.map((s) => `${s.label} — ${formatInt(s.count)}`).join(', ')

  return (
    <div className={`leitner leitner-${size}`} role="img" aria-label={`Вивчено ${percent}%: ${summary}`}>
      {size === 'large' && <p className="leitner-percent headline num">{percent}% вивчено</p>}

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
    </div>
  )
}
