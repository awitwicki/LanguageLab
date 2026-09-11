import type { ChapterView, LearningProgress } from '../api/client'

export const BOX_COUNT = 5

/**
 * Зважений відсоток: не почато = 0, бокс b = b/5, вивчено = 1. Суворий «частка вивчених»
 * два місяці стояв би на 0 (Leitner доводить слово до «вивчено» не раніше ніж за 55 днів).
 */
export function learningPercent(p: LearningProgress): number {
  if (p.total <= 0) {
    return 0
  }

  let weighted = p.learned * BOX_COUNT

  for (let box = 1; box <= BOX_COUNT; box++) {
    weighted += box * (p.boxes[box - 1] ?? 0)
  }

  return Math.min(100, Math.round((weighted / (BOX_COUNT * p.total)) * 100))
}

export type SegmentKey = 'learned' | 'box4' | 'box3' | 'box2' | 'box1' | 'new'

export interface Segment {
  key: SegmentKey
  label: string
  count: number
  /** Частка від total, 0..1. */
  share: number
}

/** Шість сегментів у порядку показу: від «вивчено» до «не почато». Бокс 5 у процесі складено з вивченими — він за одну правильну відповідь від IsLearned. */
export function learningSegments(p: LearningProgress): Segment[] {
  const counts: [SegmentKey, string, number][] = [
    ['learned', 'learned', p.learned + (p.boxes[4] ?? 0)],
    ['box4', 'box 4', p.boxes[3] ?? 0],
    ['box3', 'box 3', p.boxes[2] ?? 0],
    ['box2', 'box 2', p.boxes[1] ?? 0],
    ['box1', 'box 1', p.boxes[0] ?? 0],
    ['new', 'not started', p.notStarted],
  ]

  return counts.map(([key, label, count]) => ({ key, label, count, share: p.total > 0 ? count / p.total : 0 }))
}

/**
 * What a chapter row can offer. New words always win — that is the batch the preview is
 * built around; a review is the fallback once a chapter has been started through. `wait`
 * is the honest Leitner answer: something is in progress, but nothing is due yet.
 */
export type ChapterAction =
  | { kind: 'exercise'; learnable: number }
  | { kind: 'review'; due: number }
  | { kind: 'wait'; inProgress: number; nextDueAt: string | null }
  | { kind: 'none' }

export function chapterAction(chapter: ChapterView): ChapterAction {
  if (chapter.learnableCount > 0) {
    return { kind: 'exercise', learnable: chapter.learnableCount }
  }

  if (chapter.dueCount > 0) {
    return { kind: 'review', due: chapter.dueCount }
  }

  // Boxes hold unlearned words only, so their sum is exactly "in progress".
  const inProgress = chapter.learning.boxes.reduce((sum, n) => sum + n, 0)

  return inProgress > 0 ? { kind: 'wait', inProgress, nextDueAt: chapter.nextDueAt } : { kind: 'none' }
}
