import type { ChapterView } from '../api/client'

export const WHOLE_BOOK = 'Уся книжка'

/// fb2-секція без <title> приходить із порожньою назвою; показуємо порядковий номер.
export function chapterLabel(chapter: ChapterView): string {
  const title = chapter.title.trim()
  return title || `Глава ${chapter.order + 1}`
}
