import { BookFormatError } from '../books/formatError'

export interface SectionNode {
  title: string
  depth: number
  /** The text belonging to this section itself, without the nested ones. */
  ownText: string
  children: SectionNode[]
}

export interface ParsedBook {
  bookTitle: string
  sections: SectionNode[]
  maxDepth: number
}

export interface RawChapter {
  title: string
  text: string
}

/** 'leaf' — a chapter is a section with no nested ones; a number is a fixed depth, 1-based. */
export type ChapterMode = 'leaf' | number

/**
 * Parses the fb2 into a section tree once. Switching the nesting level on
 * the preview screen then works through flattenChapters over that same tree,
 * without parsing the file again.
 */
export function parseBook(xml: string): ParsedBook {
  const doc = new DOMParser().parseFromString(xml, 'application/xml')

  if (doc.getElementsByTagName('parsererror').length > 0) {
    throw new BookFormatError('invalid')
  }

  const bookTitle = doc.querySelector('description > title-info > book-title')?.textContent?.trim() ?? ''

  const sections: SectionNode[] = []

  for (const body of Array.from(doc.getElementsByTagName('body'))) {
    // Footnotes are not the book's text: their numbers and boilerplate would yield garbage "words".
    if (body.getAttribute('name') === 'notes') {
      continue
    }

    for (const section of directChildSections(body)) {
      sections.push(toNode(section, 1))
    }
  }

  return { bookTitle, sections, maxDepth: depthOf(sections) }
}

export function flattenChapters(sections: SectionNode[], mode: ChapterMode): RawChapter[] {
  const chapters: RawChapter[] = []

  const walk = (node: SectionNode) => {
    const isChapter =
      mode === 'leaf' ? node.children.length === 0 : node.depth >= mode || node.children.length === 0

    if (isChapter) {
      chapters.push({ title: node.title, text: collectText(node) })
      return
    }

    node.children.forEach(walk)
  }

  sections.forEach(walk)
  return chapters
}

function directChildSections(element: Element): Element[] {
  return Array.from(element.children).filter((child) => child.tagName === 'section')
}

function toNode(section: Element, depth: number): SectionNode {
  const titleElement = Array.from(section.children).find((child) => child.tagName === 'title')
  const children = directChildSections(section)

  const ownTextParts: string[] = []

  for (const child of Array.from(section.children)) {
    // <binary> never gets here — it sits outside <body>. But the title and the
    // nested sections are excluded explicitly: the title would yield the chapter
    // number as a "word", and the nested ones are collected separately.
    if (child.tagName === 'section' || child.tagName === 'title') {
      continue
    }

    ownTextParts.push(child.textContent ?? '')
  }

  return {
    title: titleElement?.textContent?.trim().replace(/\s+/g, ' ') ?? '',
    depth,
    ownText: ownTextParts.join(' '),
    children: children.map((child) => toNode(child, depth + 1)),
  }
}

/** The section's text together with all nested ones — needed when chapters merge into one. */
function collectText(node: SectionNode): string {
  return [node.ownText, ...node.children.map(collectText)].join(' ').trim()
}

function depthOf(sections: SectionNode[]): number {
  let max = 0

  const walk = (node: SectionNode) => {
    max = Math.max(max, node.depth)
    node.children.forEach(walk)
  }

  sections.forEach(walk)
  return max
}
