import { describe, expect, it } from 'vitest'
import type { ChapterView } from '../api/client'
import { WHOLE_BOOK, chapterLabel } from './labels'

const chapter = (order: number, title: string): ChapterView => ({
  id: order + 100,
  order,
  title,
  wordsCount: 1,
  sortedCount: 0,
  learnableCount: 0,
  learning: { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 },
  dueCount: 0,
  nextDueAt: null,
  isStarred: false,
})

describe('chapterLabel', () => {
  it('keeps a chapter title as is', () => {
    expect(chapterLabel(chapter(0, 'Holston'))).toBe('Holston')
  })

  it('falls back to "Chapter N", counted from 1, when untitled', () => {
    expect(chapterLabel(chapter(0, ''))).toBe('Chapter 1')
    expect(chapterLabel(chapter(11, '   '))).toBe('Chapter 12')
  })

  it('names the whole-book scope', () => {
    expect(WHOLE_BOOK).toBe('Whole book')
  })
})
