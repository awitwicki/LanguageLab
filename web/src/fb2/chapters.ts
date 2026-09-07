export interface SectionNode {
  title: string
  depth: number
  /** Текст, що належить саме цій секції, без тексту вкладених. */
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

/** 'leaf' — глава це секція без вкладених; число — фіксована глибина, 1-based. */
export type ChapterMode = 'leaf' | number

/**
 * Розбирає fb2 у дерево секцій один раз. Перемикання рівня вкладеності на
 * екрані прев'ю потім працює через flattenChapters по цьому ж дереву,
 * без повторного парсингу файлу.
 */
export function parseBook(xml: string): ParsedBook {
  const doc = new DOMParser().parseFromString(xml, 'application/xml')

  const bookTitle = doc.querySelector('description > title-info > book-title')?.textContent?.trim() ?? ''

  const sections: SectionNode[] = []

  for (const body of Array.from(doc.getElementsByTagName('body'))) {
    // Виноски — не текст книжки: їх номери й службові хвости дали б сміттєві «слова».
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
    // <binary> сюди не потрапляє — він лежить поза <body>. Але заголовок і
    // вкладені секції виключаємо явно: заголовок дасть номер глави як «слово»,
    // а вкладені зберуться окремо.
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

/** Текст секції разом з усіма вкладеними — потрібен, коли глави злипаються в одну. */
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
