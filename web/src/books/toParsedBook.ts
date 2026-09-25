import { parseBook, type ParsedBook, type SectionNode } from '../fb2/chapters'
import type { EpubBook } from './epub'
import type { BookSource } from './format'

/**
 * A book file → the import pipeline's section tree. An epub's spine is flat, so every document
 * becomes one depth-1 section: `flattenChapters` then yields one chapter per document at any
 * level, and the whole pipeline below (the word extractor, the upload) is unchanged.
 */
export function toParsedBook(source: BookSource): ParsedBook {
  return source.format === 'fb2' ? parseBook(source.xml) : fromEpub(source.book)
}

function fromEpub(book: EpubBook): ParsedBook {
  const sections: SectionNode[] = book.docs.map((doc) => ({
    title: doc.title,
    depth: 1,
    ownText: doc.paragraphs.join(' '),
    children: [],
  }))

  return { bookTitle: book.title, sections, maxDepth: 1 }
}
