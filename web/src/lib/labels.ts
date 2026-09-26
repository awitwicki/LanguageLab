import { formatInt } from './format'

export const WHOLE_BOOK = 'Whole book'

/// A review with no book of its own spans the lot.
export const ALL_DICTIONARIES = 'All dictionaries'

/// An fb2 section without a <title> arrives with an empty name; show its ordinal number.
/// Takes only the two fields a label needs, so both a `ChapterView` and the lighter
/// `ScopeChapter` of the home screen's recent lists can be named the same way.
export function chapterLabel(chapter: { order: number; title: string }): string {
  const title = chapter.title.trim()
  return title || `Chapter ${chapter.order + 1}`
}

/// Names a scope on the home screen's recent lists: the book, narrowed by a chapter when
/// there is one. A missing book name means the scope was every dictionary at once.
export function scopeLabel(bookName: string | null, chapter: { order: number; title: string } | null): string {
  const book = bookName ?? ALL_DICTIONARIES
  return chapter ? `${book} · ${chapterLabel(chapter)}` : book
}

/// How far into a book the reader got — for the library's rows and the home screen's
/// "Continue". Takes only the three fields it prints, so it needs no import of `ReaderBookDto`.
export function readingProgressLabel(book: { chapterIndex: number; chaptersCount: number; progress: number }): string {
  const chapter = `Chapter ${formatInt(book.chapterIndex + 1)} of ${formatInt(book.chaptersCount)}`

  return `${chapter} · ${formatInt(Math.round(book.progress * 100))} %`
}
