import type { EpubBook } from '../books/epub'
import { readBookSource } from '../books/format'
import { BookFormatError } from '../books/formatError'

/** One piece of a sentence: a word, or the text between words (spaces, punctuation). */
export interface Token {
  text: string
  isWord: boolean
}

export interface ReaderSentence {
  text: string
  tokens: Token[]
}

export interface ReaderParagraph {
  sentences: ReaderSentence[]
}

export interface ReaderChapter {
  /** Empty when the section had no <title>. */
  title: string
  paragraphs: ReaderParagraph[]
}

export interface ReaderBook {
  title: string
  author: string
  chapters: ReaderChapter[]
}

/** Where the reader is: all 0-based. The same triple the server stores (ReaderBook). */
export interface ReaderPosition {
  chapterIndex: number
  paragraphIndex: number
  sentenceIndex: number
}

/** Elements whose text is one paragraph of the reader. <v> is a poem line. */
const PARAGRAPH_TAGS = new Set(['p', 'v', 'subtitle', 'text-author'])

/** Titles Intl.Segmenter mistakes for a sentence end ("Mr. | Smith"). Lowercase, without the dot. */
const ABBREVIATION_END = /(?:^|[\s(])(?:mr|mrs|ms|dr|st|mt|jr|sr|prof|gen|col|capt|lt|sgt|vs)\.$/i

const sentenceSegmenter = new Intl.Segmenter('en', { granularity: 'sentence' })
const wordSegmenter = new Intl.Segmenter('en', { granularity: 'word' })

/**
 * A file's bytes → the book. `books/format.ts` decides what the file is (fb2, a zipped fb2, or an
 * epub) and this only maps that onto the reader's shape. Synchronous: fflate's unzipSync and
 * DOMParser both are, and workers do not start inside Telegram's web view.
 */
export function readBookFile(buffer: ArrayBuffer, fileName: string): ReaderBook {
  const source = readBookSource(buffer, fileName)

  return source.format === 'fb2' ? parseReaderBook(source.xml, source.fallbackTitle) : readerBookFromEpub(source.book)
}

/** An epub's documents are its chapters; sentence splitting is the same for both formats. */
function readerBookFromEpub(epub: EpubBook): ReaderBook {
  const chapters: ReaderChapter[] = []

  for (const doc of epub.docs) {
    const paragraphs: ReaderParagraph[] = []

    for (const text of doc.paragraphs) {
      const sentences = splitSentences(text)

      if (sentences.length > 0) {
        paragraphs.push({ sentences })
      }
    }

    if (paragraphs.length > 0) {
      chapters.push({ title: doc.title, paragraphs })
    }
  }

  if (chapters.length === 0) {
    throw new BookFormatError('invalid')
  }

  return { title: epub.title, author: epub.author, chapters }
}

/**
 * Chapters are the sections with text of their own, in book order — a part heading with only
 * nested sections under it yields none. Unlike the import's parser (fb2/chapters.ts) this keeps
 * paragraphs and sentences, because the reader shows them. Runs on the main thread: workers do
 * not start inside Telegram's web view, and DOMParser is fast.
 */
export function parseReaderBook(xml: string, fallbackTitle: string): ReaderBook {
  const doc = new DOMParser().parseFromString(xml, 'application/xml')

  if (doc.getElementsByTagName('parsererror').length > 0) {
    throw new BookFormatError('invalid')
  }

  const titleInfo = doc.querySelector('description > title-info')
  const title = titleInfo?.querySelector('book-title')?.textContent?.trim() || fallbackTitle
  const authorElement = titleInfo?.querySelector('author')
  const author = authorElement
    ? ['first-name', 'middle-name', 'last-name']
        .map((tag) => authorElement.querySelector(tag)?.textContent?.trim() ?? '')
        .filter(Boolean)
        .join(' ')
    : ''

  const chapters: ReaderChapter[] = []

  for (const body of Array.from(doc.getElementsByTagName('body'))) {
    // Footnotes are not the book's text.
    if (body.getAttribute('name') === 'notes') {
      continue
    }

    collectChapters(body, chapters)
  }

  if (chapters.length === 0) {
    throw new BookFormatError('invalid')
  }

  return { title, author, chapters }
}

function collectChapters(section: Element, into: ReaderChapter[]) {
  const children = Array.from(section.children)
  const titleElement = children.find((child) => child.tagName === 'title')
  const paragraphs: ReaderParagraph[] = []

  for (const child of children) {
    if (child.tagName === 'section' || child.tagName === 'title') {
      continue
    }

    for (const text of paragraphTexts(child)) {
      const sentences = splitSentences(text)

      if (sentences.length > 0) {
        paragraphs.push({ sentences })
      }
    }
  }

  if (paragraphs.length > 0) {
    into.push({ title: titleElement?.textContent?.trim().replace(/\s+/g, ' ') ?? '', paragraphs })
  }

  for (const child of children) {
    if (child.tagName === 'section') {
      collectChapters(child, into)
    }
  }
}

function paragraphTexts(element: Element): string[] {
  if (PARAGRAPH_TAGS.has(element.tagName)) {
    return [element.textContent ?? '']
  }

  return Array.from(element.children).flatMap(paragraphTexts)
}

export function splitSentences(raw: string): ReaderSentence[] {
  const text = raw.replace(/\s+/g, ' ').trim()

  if (text === '') {
    return []
  }

  const merged: string[] = []

  for (const { segment } of sentenceSegmenter.segment(text)) {
    const piece = segment.trim()

    if (piece === '') {
      continue
    }

    const last = merged.at(-1)

    // Two repairs to the segmenter: "Mr. | Smith" and '"Hi!" | and left.' are one sentence.
    if (last !== undefined && (ABBREVIATION_END.test(last) || /^[a-z]/.test(piece))) {
      merged[merged.length - 1] = `${last} ${piece}`
    } else {
      merged.push(piece)
    }
  }

  return merged.map((sentence) => ({ text: sentence, tokens: tokenize(sentence) }))
}

function tokenize(sentence: string): Token[] {
  return Array.from(wordSegmenter.segment(sentence), (segment) => ({
    text: segment.segment,
    isWord: segment.isWordLike ?? false,
  }))
}

export function positionKey(position: ReaderPosition): string {
  return `${position.chapterIndex}.${position.paragraphIndex}.${position.sentenceIndex}`
}

export function parsePositionKey(key: string): ReaderPosition | null {
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(key)

  return match
    ? { chapterIndex: Number(match[1]), paragraphIndex: Number(match[2]), sentenceIndex: Number(match[3]) }
    : null
}

export function comparePositions(a: ReaderPosition, b: ReaderPosition): number {
  return a.chapterIndex - b.chapterIndex || a.paragraphIndex - b.paragraphIndex || a.sentenceIndex - b.sentenceIndex
}

/** A stored position may point past the end of an edited or differently parsed book: fall back to a chapter start. */
export function clampPosition(book: ReaderBook, position: ReaderPosition): ReaderPosition {
  const chapterIndex = Math.min(Math.max(0, position.chapterIndex), book.chapters.length - 1)
  const start = { chapterIndex, paragraphIndex: 0, sentenceIndex: 0 }

  if (chapterIndex !== position.chapterIndex) {
    return start
  }

  const paragraph = book.chapters[chapterIndex].paragraphs[position.paragraphIndex]

  if (!paragraph || position.sentenceIndex < 0 || position.sentenceIndex >= paragraph.sentences.length) {
    return start
  }

  return position
}

/** Sentences before the position over all sentences, 0..1. */
export function bookProgress(book: ReaderBook, position: ReaderPosition): number {
  let before = 0
  let total = 0

  book.chapters.forEach((chapter, chapterIndex) =>
    chapter.paragraphs.forEach((paragraph, paragraphIndex) =>
      paragraph.sentences.forEach((_, sentenceIndex) => {
        if (comparePositions({ chapterIndex, paragraphIndex, sentenceIndex }, position) < 0) {
          before++
        }

        total++
      }),
    ),
  )

  return total === 0 ? 0 : before / total
}

/** The same within the position's chapter — the bar under the reader's header. */
export function chapterProgress(book: ReaderBook, position: ReaderPosition): number {
  return bookProgress({ ...book, chapters: [book.chapters[position.chapterIndex]] }, { ...position, chapterIndex: 0 })
}
