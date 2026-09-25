import { positionKey, type ReaderChapter, type ReaderPosition, type ReaderSentence } from './readerBook'

/** One rendered line of the reader: a sentence, where it is, and whether it opens a paragraph. */
export interface ChapterSentence {
  key: string
  sentence: ReaderSentence
  paragraphStart: boolean
}

/**
 * Sentences per chunk — the unit the reader puts in the DOM. A chapter is read through a few
 * chunks around the place being read; the rest stand as gaps of estimated height until they come
 * near the screen, so opening a chapter costs a few screens of markup instead of all of it.
 */
export const CHUNK_SENTENCES = 20

/** Chunks mounted on each side of the one being read when a chapter opens.  */
export const CHUNK_RADIUS = 1

/** The chapter's sentences in reading order, cut into chunks. */
export function chunkChapter(chapter: ReaderChapter, chapterIndex: number, size = CHUNK_SENTENCES): ChapterSentence[][] {
  const chunks: ChapterSentence[][] = []
  let current: ChapterSentence[] = []

  chapter.paragraphs.forEach((paragraph, paragraphIndex) =>
    paragraph.sentences.forEach((sentence, sentenceIndex) => {
      if (current.length === size) {
        chunks.push(current)
        current = []
      }

      current.push({
        key: positionKey({ chapterIndex, paragraphIndex, sentenceIndex }),
        sentence,
        paragraphStart: sentenceIndex === 0 && paragraphIndex > 0,
      })
    }),
  )

  if (current.length > 0) {
    chunks.push(current)
  }

  return chunks
}

/**
 * Which chunk holds a position — the chunk the reader opens around. A position past the end of
 * the chapter (a book re-parsed since it was stored) gives the last chunk.
 */
export function chunkOfPosition(chapter: ReaderChapter, position: ReaderPosition, size = CHUNK_SENTENCES): number {
  const sentencesIn = (paragraphs: ReaderChapter['paragraphs']) =>
    paragraphs.reduce((sum, paragraph) => sum + paragraph.sentences.length, 0)

  const before = sentencesIn(chapter.paragraphs.slice(0, position.paragraphIndex))
  const last = Math.max(0, Math.ceil(sentencesIn(chapter.paragraphs) / size) - 1)

  return Math.min(Math.floor((before + Math.max(0, position.sentenceIndex)) / size), last)
}

/** The chunks around one, clamped to the chapter. */
export function chunksAround(chunk: number, count: number, radius = CHUNK_RADIUS): number[] {
  const first = Math.max(0, chunk - radius)
  const last = Math.min(count - 1, chunk + radius)

  return Array.from({ length: Math.max(0, last - first + 1) }, (_, index) => first + index)
}

/** Every chunk — what a browser without IntersectionObserver gets, the whole chapter at once. */
export function allChunks(count: number): number[] {
  return Array.from({ length: count }, (_, index) => index)
}
