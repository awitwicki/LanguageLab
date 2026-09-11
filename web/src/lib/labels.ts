import type { ChapterView } from '../api/client'

export const WHOLE_BOOK = 'Whole book'

/// fb2-секція без <title> приходить із порожньою назвою; показуємо порядковий номер.
export function chapterLabel(chapter: ChapterView): string {
  const title = chapter.title.trim()
  return title || `Chapter ${chapter.order + 1}`
}
