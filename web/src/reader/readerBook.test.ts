import { describe, expect, it } from 'vitest'
import { EPUB3_FILES, epub3Bytes, zipBytes } from '../test/epubFixtures'
import { bytesOf, READER_BOOK_XML } from '../test/readerFixtures'
import { BookFormatError } from '../books/formatError'
import {
  bookProgress,
  chapterProgress,
  clampPosition,
  comparePositions,
  parsePositionKey,
  parseReaderBook,
  positionKey,
  readBookFile,
  splitSentences,
} from './readerBook'

const book = parseReaderBook(READER_BOOK_XML, 'fallback')

describe('parseReaderBook', () => {
  it('takes the title and the author from title-info', () => {
    expect(book.title).toBe("Death's End")
    expect(book.author).toBe('Cixin Liu')
  })

  it('makes a chapter of every section with text of its own, skipping the notes', () => {
    expect(book.chapters.map((c) => c.title)).toEqual(['The Swordholder', 'Year 62'])
  })

  it('keeps paragraphs and splits them into sentences', () => {
    const [first, second] = book.chapters[0].paragraphs

    expect(first.sentences.map((s) => s.text)).toEqual([
      'Compared to the beginning, fewer individuals were emerging.',
      'They still formed a stratum.',
    ])
    expect(second.sentences.map((s) => s.text)).toEqual(['All of them had some difficulty reintegrating.'])
  })

  it('makes every poem line a paragraph and drops empty lines', () => {
    expect(book.chapters[1].paragraphs.map((p) => p.sentences.map((s) => s.text).join(' '))).toEqual([
      'The silo was quiet,',
      'the silo was cold.',
      'Most men tried to adjust.',
    ])
  })

  it('falls back to the file name without a book-title', () => {
    const bare = READER_BOOK_XML.replace("<book-title>Death's End</book-title>", '')

    expect(parseReaderBook(bare, 'deaths-end').title).toBe('deaths-end')
  })

  it('refuses broken XML and a book without text', () => {
    expect(() => parseReaderBook('<a><b></a>', 'x')).toThrow(BookFormatError)
    expect(() => parseReaderBook('<FictionBook><body></body></FictionBook>', 'x')).toThrow(
      "This file isn't a readable fb2 or epub book.",
    )
  })
})

describe('readBookFile', () => {
  it('reads windows-1251 books the way the import does', () => {
    const head = '<?xml version="1.0" encoding="windows-1251"?><FictionBook><body><section><p>'
    const tail = ' said the guard.</p></section></body></FictionBook>'
    // "Привіт" in windows-1251.
    const bytes = new Uint8Array([
      ...new TextEncoder().encode(head),
      0xcf, 0xf0, 0xe8, 0xe2, 0xb3, 0xf2,
      ...new TextEncoder().encode(tail),
    ]).buffer

    expect(readBookFile(bytes, 'guard.fb2').chapters[0].paragraphs[0].sentences[0].text).toBe('Привіт said the guard.')
  })

  it('reads an epub as a book of its own', () => {
    const epub = readBookFile(epub3Bytes(), 'deaths-end.epub')

    expect(epub.title).toBe("Death's End")
    expect(epub.author).toBe('Cixin Liu')
    expect(epub.chapters.map((c) => c.title)).toEqual(['The Swordholder', 'Year 62', 'Chapter Three'])
    expect(epub.chapters[0].paragraphs[0].sentences.map((s) => s.text)).toEqual([
      'Compared to the beginning, fewer individuals were emerging.',
    ])
    expect(epub.chapters[0].paragraphs).toHaveLength(2)
  })

  it('opens a zipped fb2 instead of asking for it to be unzipped', () => {
    const zip = zipBytes({ 'deaths-end.fb2': READER_BOOK_XML })

    expect(readBookFile(zip, 'deaths-end.fb2.zip').chapters.map((c) => c.title)).toEqual([
      'The Swordholder',
      'Year 62',
    ])
  })

  it('names an epub after the file when its metadata has no title', () => {
    const opf = (EPUB3_FILES['OEBPS/content.opf'] as string).replace("<dc:title>Death's End</dc:title>", '')

    expect(readBookFile(epub3Bytes({ 'OEBPS/content.opf': opf }), 'wool.epub').title).toBe('wool')
  })

  it('names the book after the file when it has no title', () => {
    const xml = '<FictionBook><body><section><p>Hello there.</p></section></body></FictionBook>'

    expect(readBookFile(bytesOf(xml), 'wool.fb2').title).toBe('wool')
  })
})

describe('splitSentences', () => {
  it('keeps "Mr." and a lowercase continuation inside one sentence', () => {
    expect(splitSentences('Mr. Smith said "Hi!" and left. Then  silence.').map((s) => s.text)).toEqual([
      'Mr. Smith said "Hi!" and left.',
      'Then silence.',
    ])
  })

  it('tokenizes into words and the text between them, losing nothing', () => {
    const [sentence] = splitSentences("Don't adjust.")

    expect(sentence.tokens).toEqual([
      { text: "Don't", isWord: true },
      { text: ' ', isWord: false },
      { text: 'adjust', isWord: true },
      { text: '.', isWord: false },
    ])
    expect(sentence.tokens.map((t) => t.text).join('')).toBe(sentence.text)
  })

  it('yields nothing for whitespace', () => {
    expect(splitSentences('  \n ')).toEqual([])
  })
})

describe('positions', () => {
  it('round-trip through a key', () => {
    const position = { chapterIndex: 1, paragraphIndex: 2, sentenceIndex: 3 }

    expect(positionKey(position)).toBe('1.2.3')
    expect(parsePositionKey('1.2.3')).toEqual(position)
    expect(parsePositionKey('nope')).toBeNull()
  })

  it('compare in reading order', () => {
    const at = (c: number, p: number, s: number) => ({ chapterIndex: c, paragraphIndex: p, sentenceIndex: s })

    expect(comparePositions(at(0, 1, 0), at(0, 0, 5))).toBeGreaterThan(0)
    expect(comparePositions(at(0, 0, 1), at(1, 0, 0))).toBeLessThan(0)
    expect(comparePositions(at(1, 1, 1), at(1, 1, 1))).toBe(0)
  })

  it('clamp to the start of the nearest chapter when out of range', () => {
    expect(clampPosition(book, { chapterIndex: 9, paragraphIndex: 4, sentenceIndex: 4 })).toEqual({
      chapterIndex: 1,
      paragraphIndex: 0,
      sentenceIndex: 0,
    })
    expect(clampPosition(book, { chapterIndex: 0, paragraphIndex: 5, sentenceIndex: 0 })).toEqual({
      chapterIndex: 0,
      paragraphIndex: 0,
      sentenceIndex: 0,
    })
    expect(clampPosition(book, { chapterIndex: 0, paragraphIndex: 0, sentenceIndex: 1 })).toEqual({
      chapterIndex: 0,
      paragraphIndex: 0,
      sentenceIndex: 1,
    })
  })

  it('measure progress in sentences', () => {
    // 3 sentences in chapter 0, 3 in chapter 1.
    expect(bookProgress(book, { chapterIndex: 1, paragraphIndex: 0, sentenceIndex: 0 })).toBe(0.5)
    expect(chapterProgress(book, { chapterIndex: 0, paragraphIndex: 1, sentenceIndex: 0 })).toBeCloseTo(2 / 3)
  })
})
