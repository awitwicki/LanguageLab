import { describe, expect, it } from 'vitest'
import type { ChapterView } from '../api/client'
import { WHOLE_BOOK, chapterLabel } from './labels'

const chapter = (order: number, title: string): ChapterView => ({
  id: order + 100,
  order,
  title,
  wordsCount: 1,
  sortedCount: 0,
})

describe('chapterLabel', () => {
  it('назва глави — як є', () => {
    expect(chapterLabel(chapter(0, 'Holston'))).toBe('Holston')
  })

  it('без назви — «Глава N», де N рахується з 1', () => {
    expect(chapterLabel(chapter(0, ''))).toBe('Глава 1')
    expect(chapterLabel(chapter(11, '   '))).toBe('Глава 12')
  })

  it('заголовок для всієї книжки', () => {
    expect(WHOLE_BOOK).toBe('Уся книжка')
  })
})
