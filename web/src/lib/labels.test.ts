import { describe, expect, it } from 'vitest'
import type { ChapterView } from '../api/client'
import { ALL_DICTIONARIES, WHOLE_BOOK, chapterLabel, scopeLabel } from './labels'

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

describe('scopeLabel', () => {
  it('narrows a book by its chapter', () => {
    expect(scopeLabel('Wool', chapter(0, 'Holston'))).toBe('Wool · Holston')
  })

  it('is the book alone for a whole-book scope', () => {
    expect(scopeLabel('Wool', null)).toBe('Wool')
  })

  it('numbers an untitled chapter here too', () => {
    expect(scopeLabel('Wool', chapter(3, ''))).toBe('Wool · Chapter 4')
  })

  /// A review started from the home screen belongs to no book.
  it('falls back to every dictionary when there is no book', () => {
    expect(scopeLabel(null, null)).toBe(ALL_DICTIONARIES)
  })
})
