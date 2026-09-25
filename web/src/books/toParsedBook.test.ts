import { describe, expect, it } from 'vitest'
import { epub3Bytes } from '../test/epubFixtures'
import { READER_BOOK_XML, bytesOf } from '../test/readerFixtures'
import { flattenChapters } from '../fb2/chapters'
import { readBookSource } from './format'
import { BookFormatError } from './formatError'
import { toParsedBook } from './toParsedBook'

const epub = toParsedBook(readBookSource(epub3Bytes(), 'deaths-end.epub'))

describe('toParsedBook', () => {
  it('keeps the fb2 tree as the fb2 parser builds it', () => {
    const fb2 = toParsedBook(readBookSource(bytesOf(READER_BOOK_XML), 'deaths-end.fb2'))

    expect(fb2.bookTitle).toBe("Death's End")
    expect(fb2.maxDepth).toBe(2)
  })

  it('gives an epub one flat section per document', () => {
    expect(epub.bookTitle).toBe("Death's End")
    expect(epub.maxDepth).toBe(1)
    expect(epub.sections.map((s) => s.title)).toEqual(['The Swordholder', 'Year 62', 'Chapter Three'])
    expect(epub.sections.every((s) => s.depth === 1 && s.children.length === 0)).toBe(true)
  })

  it('puts every paragraph of a document into its section text', () => {
    expect(epub.sections[0].ownText).toBe(
      'Compared to the beginning, fewer individuals were emerging. They still formed a stratum.',
    )
  })

  it('gives the import pipeline one chapter per document', () => {
    expect(flattenChapters(epub.sections, 'leaf').map((c) => c.title)).toEqual([
      'The Swordholder',
      'Year 62',
      'Chapter Three',
    ])
  })

  it('refuses a file that is not a book at all rather than showing an empty preview', () => {
    expect(() => toParsedBook(readBookSource(bytesOf('<not a book'), 'junk.fb2'))).toThrow(BookFormatError)
    expect(() => toParsedBook(readBookSource(new ArrayBuffer(0), 'empty.fb2'))).toThrow(
      "This file isn't a readable fb2 or epub book.",
    )
  })
})
