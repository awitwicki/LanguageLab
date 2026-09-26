import type { LearningProgress } from '../api/client'
import { formatProgress, percentOf } from '../lib/format'
import { LEARNED_CAPTION, LeitnerScale } from './LeitnerScale'
import { ProgressBar } from './ProgressBar'
import './ScopeProgress.css'

interface Props {
  /** The sorting row; left out for a scope that is never sorted (the personal dictionary). */
  sorting?: { sorted: number; total: number }
  learning: LearningProgress
}

/**
 * A scope's two bars on one grid: "Sorted" (how much of the vocabulary has been sorted)
 * over "Learned" (the Leitner scale). Both tracks start and end on the same columns, so
 * the eye reads them as one instrument; the captions sit under the block, not inside a row.
 */
export function ScopeProgress({ sorting, learning }: Props) {
  const hasLearning = learning.total > 0

  if (!sorting && !hasLearning) {
    return null
  }

  return (
    <div className="scope-progress">
      {sorting && (
        <div className="progress-row progress-row-sorted">
          <span className="scope-label footnote">Sorted</span>
          <ProgressBar sorted={sorting.sorted} total={sorting.total} showLabel={false} />
          <span className="scope-value footnote num">{percentOf(sorting.sorted, sorting.total)}%</span>
        </div>
      )}

      {hasLearning && (
        <div className="progress-row">
          <span className="scope-label footnote">Learned</span>
          <LeitnerScale progress={learning} />
        </div>
      )}

      {sorting && <p className="scope-caption footnote num">{formatProgress(sorting.sorted, sorting.total)} words sorted</p>}
      {hasLearning && <p className="scope-caption footnote">{LEARNED_CAPTION}</p>}
    </div>
  )
}
