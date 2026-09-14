import type { ChapterView } from '../api/client'

export const WHOLE_BOOK = 'Whole book'

/// An fb2 section without a <title> arrives with an empty name; show its ordinal number.
export function chapterLabel(chapter: ChapterView): string {
  const title = chapter.title.trim()
  return title || `Chapter ${chapter.order + 1}`
}
