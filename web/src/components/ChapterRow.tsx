import type { ChapterView } from '../api/client'
import { formatDue, formatInt, percentOf, wordsLabel } from '../lib/format'
import { chapterLabel } from '../lib/labels'
import { chapterAction, type ChapterAction } from '../lib/learning'
import { LeitnerScale } from './LeitnerScale'
import './ChapterRow.css'

interface Props {
  chapter: ChapterView
  /** No Leitner scale: the starred lists are for reaching a chapter, not for studying its standing. */
  compact?: boolean
  reviewBusy: boolean
  starBusy: boolean
  /** The main click. */
  onSort: () => void
  /** The side button when the chapter still has new words. */
  onTrain: () => void
  /** The side button once it has none but some are due. */
  onReview: () => void
  onToggleStar: () => void
}

/**
 * One chapter, as three separate buttons rather than nested ones: the main area (sort), the
 * side action (exercise or review) and the star. Rendered as an <li>: put it in a `.chapter-list`.
 * Shared by the book page's full list, its starred mini-list and the home screen.
 */
export function ChapterRow({ chapter, compact = false, reviewBusy, starBusy, onSort, onTrain, onReview, onToggleStar }: Props) {
  const label = chapterLabel(chapter)
  const percent = percentOf(chapter.sortedCount, chapter.wordsCount)
  const action = chapterAction(chapter)

  return (
    <li className={`chapter-row${percent === 100 ? ' done' : ''}${compact ? ' compact' : ''}`}>
      <button type="button" className="chapter-main" onClick={onSort}>
        <span className="chapter-text">
          <span className="chapter-title">{label}</span>
          <span className="chapter-sub num">
            {wordsLabel(chapter.wordsCount)} · {chapterSubLine(action)}
          </span>
        </span>
        <span className="chapter-pct num">{percent}%</span>
        <span className="chevron" aria-hidden="true">
          ›
        </span>
      </button>
      {action.kind === 'review' ? (
        <button
          type="button"
          className="btn btn-quiet chapter-train"
          disabled={reviewBusy}
          aria-label={`Review: ${label}`}
          onClick={onReview}
        >
          Review
        </button>
      ) : (
        <button
          type="button"
          className="btn btn-quiet chapter-train"
          disabled={action.kind !== 'exercise'}
          title={
            action.kind === 'wait' ? 'Nothing due yet in this chapter'
            : action.kind === 'none' ? 'No words to learn in this chapter'
            : undefined
          }
          aria-label={`Exercise: ${label}`}
          onClick={onTrain}
        >
          Exercise
        </button>
      )}
      <button
        type="button"
        className="btn btn-quiet chapter-star"
        aria-pressed={chapter.isStarred}
        aria-label={`${chapter.isStarred ? 'Unstar' : 'Star'}: ${label}`}
        title={chapter.isStarred ? 'Remove from the home screen' : 'Keep this chapter on the home screen'}
        disabled={starBusy}
        onClick={onToggleStar}
      >
        {chapter.isStarred ? '★' : '☆'}
      </button>
      {/* The third line sits outside the sort button so the scale does not pollute its accessible name. */}
      {!compact && chapter.learning.total > 0 && (
        <div className="chapter-learning">
          <LeitnerScale progress={chapter.learning} />
        </div>
      )}
    </li>
  )
}

/** The second half of a chapter's sub-line: what the row's button is about to offer, or why it can't. */
function chapterSubLine(action: ChapterAction): string {
  switch (action.kind) {
    case 'exercise':
      return `${formatInt(action.learnable)} to learn`
    case 'review':
      return `${formatInt(action.due)} to review`
    case 'wait':
      return action.nextDueAt
        ? `${formatInt(action.inProgress)} in progress · next review ${formatDue(action.nextDueAt, false, new Date())}`
        : `${formatInt(action.inProgress)} in progress`
    case 'none':
      return '0 to learn'
  }
}
