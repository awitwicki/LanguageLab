import { decodeXml } from './decode'
import { BookFormatError } from './formatError'
import type { ZipEntries } from './zip'

/** Elements whose text is one paragraph of the book — the epub counterpart of fb2's <p> and <v>. */
const PARAGRAPH_TAGS = new Set(['p', 'li', 'blockquote', 'pre', 'figcaption'])

/**
 * Never the book's text: a heading is the chapter's title (fb2's parser leaves <title> out for the
 * same reason), and navigation, scripts and footnote boilerplate would enter the dictionary as words.
 */
const SKIP_TAGS = new Set(['h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'script', 'style', 'nav'])

const NOTE_TYPES = new Set(['footnote', 'endnote', 'rearnote', 'noteref'])

const OPS_NS = 'http://www.idpf.org/2007/ops'

const TEXT_NODE = 3
const ELEMENT_NODE = 1

function elements(root: Document | Element): Element[] {
  return Array.from(root.getElementsByTagName('*'))
}

function nameOf(element: Element): string {
  return element.localName.toLowerCase()
}

/** epub:type, whether or not the document declares the prefix. */
function epubType(element: Element): string[] {
  const value = element.getAttributeNS(OPS_NS, 'type') ?? element.getAttribute('epub:type') ?? ''

  return value.toLowerCase().split(/\s+/).filter(Boolean)
}

function skipped(element: Element): boolean {
  return SKIP_TAGS.has(nameOf(element)) || epubType(element).some((type) => NOTE_TYPES.has(type))
}

/**
 * XHTML first; an epub whose documents are served as text/html is not always well-formed XML, and
 * refusing those would lose readable books, so fall back to the HTML parser.
 */
function parseDocument(source: string): Document | null {
  const xml = new DOMParser().parseFromString(source, 'application/xml')

  if (xml.getElementsByTagName('parsererror').length === 0) {
    return xml
  }

  const html = new DOMParser().parseFromString(source, 'text/html')

  return html.body ? html : null
}

function bodyOf(doc: Document): Element {
  return elements(doc).find((element) => nameOf(element) === 'body') ?? doc.documentElement
}

/**
 * An element's text, depth-first, with a separator wherever plain concatenation would run two
 * words together: at <br> (a hard line break) and around a nested paragraph tag (a <p> inside a
 * <blockquote>, an <li> holding its own <p>). Skipped elements (headings, scripts, footnotes…)
 * contribute nothing, even nested inside the paragraph this is called on.
 */
function textWithBreaks(node: Node): string {
  if (node.nodeType === TEXT_NODE) {
    return node.textContent ?? ''
  }

  if (node.nodeType !== ELEMENT_NODE) {
    return ''
  }

  const element = node as Element

  if (skipped(element)) {
    return ''
  }

  if (nameOf(element) === 'br') {
    return ' '
  }

  const inner = Array.from(element.childNodes)
    .map((child) => textWithBreaks(child))
    .join('')

  return PARAGRAPH_TAGS.has(nameOf(element)) ? ` ${inner} ` : inner
}

/**
 * A document in reading order: a paragraph's text, or the place where an element with one of the
 * TOC's fragment ids starts — the only way a TOC says where a chapter inside a document begins.
 */
type Block = { text: string } | { cut: string }

/**
 * The fragment ids of `element` that the TOC points at, pushed as cuts. `deep` also takes its
 * descendants', for an element the walk does not descend into: an anchor inside a paragraph cuts
 * before that paragraph, and one on a skipped heading (the usual place for it) still counts.
 */
function markCuts(element: Element, cuts: ReadonlySet<string>, into: Block[], deep: boolean) {
  if (cuts.size === 0) {
    return
  }

  for (const candidate of deep ? [element, ...elements(element)] : [element]) {
    const id = candidate.getAttribute('id')

    if (id && cuts.has(id)) {
      into.push({ cut: id })
    }
  }
}

function collectParagraphs(element: Element, cuts: ReadonlySet<string>, into: Block[]) {
  for (const child of Array.from(element.children)) {
    if (skipped(child)) {
      markCuts(child, cuts, into, true)
      continue
    }

    if (PARAGRAPH_TAGS.has(nameOf(child))) {
      markCuts(child, cuts, into, true)
      into.push({ text: textWithBreaks(child) })
      continue
    }

    markCuts(child, cuts, into, false)
    collectParagraphs(child, cuts, into)
  }
}

/** Some books use a <div> per paragraph and no <p> at all: take every element holding its own text. */
function collectTextBlocks(element: Element, cuts: ReadonlySet<string>, into: Block[]) {
  for (const child of Array.from(element.children)) {
    if (skipped(child)) {
      markCuts(child, cuts, into, true)
      continue
    }

    const ownText = Array.from(child.childNodes).some(
      (node) => node.nodeType === TEXT_NODE && (node.textContent ?? '').trim() !== '',
    )

    if (ownText) {
      markCuts(child, cuts, into, true)
      into.push({ text: textWithBreaks(child) })
    } else {
      markCuts(child, cuts, into, false)
      collectTextBlocks(child, cuts, into)
    }
  }
}

type Collector = (element: Element, cuts: ReadonlySet<string>, into: Block[]) => void

function collected(body: Element, cuts: ReadonlySet<string>, collect: Collector): Block[] {
  const raw: Block[] = []

  collect(body, cuts, raw)

  return raw
    .map((block) => ('text' in block ? { text: block.text.replace(/\s+/g, ' ').trim() } : block))
    .filter((block) => !('text' in block) || block.text !== '')
}

function blocksOf(doc: Document, cuts: ReadonlySet<string>): Block[] {
  const body = bodyOf(doc)
  // The fallback is decided on what is left after trimming: a document of empty <p> elements
  // has paragraph tags but no text, and is as good as a document that has none.
  const blocks = collected(body, cuts, collectParagraphs)

  return blocks.some((block) => 'text' in block) ? blocks : collected(body, cuts, collectTextBlocks)
}

function textsOf(blocks: Block[]): string[] {
  return blocks.flatMap((block) => ('text' in block ? [block.text] : []))
}

/** One XHTML document's paragraphs. Exported for its tests; parseEpub works on the parsed document. */
export function documentParagraphs(xhtml: string): string[] {
  const doc = parseDocument(xhtml)

  return doc ? textsOf(blocksOf(doc, new Set())) : []
}

/**
 * One chapter: an XHTML document of the spine, or the part of one between two places its TOC
 * points at.
 */
export interface EpubDocument {
  /** Empty when neither the TOC nor a heading names it — shown as an ordinal, as for fb2. */
  title: string
  paragraphs: string[]
}

export interface EpubBook {
  title: string
  author: string
  docs: EpubDocument[]
}

interface ManifestItem {
  path: string
  mediaType: string
  properties: string[]
}

const XHTML_TYPES = new Set(['application/xhtml+xml', 'text/html'])

/** Documents that are apparatus, not the book — the epub counterpart of fb2's <body name="notes">. */
const APPARATUS_TYPES = new Set(['toc', 'landmarks', 'footnotes', 'endnotes', 'rearnotes'])

function firstByName(root: Document | Element, name: string): Element | null {
  return elements(root).find((element) => nameOf(element) === name) ?? null
}

function allByName(root: Document | Element, name: string): Element[] {
  return elements(root).filter((element) => nameOf(element) === name)
}

function dirOf(path: string): string {
  const slash = path.lastIndexOf('/')

  return slash < 0 ? '' : path.slice(0, slash + 1)
}

function percentDecoded(raw: string): string {
  try {
    return decodeURIComponent(raw)
  } catch {
    // A malformed % sequence: the href stands as written.
    return raw
  }
}

/** An href inside the archive: the fragment dropped, percent-decoded, "." and ".." collapsed. */
function resolvePath(base: string, href: string): string {
  const decoded = percentDecoded(href.split('#')[0])
  const segments: string[] = []

  for (const segment of `${base}${decoded}`.split('/')) {
    if (segment === '' || segment === '.') {
      continue
    }

    if (segment === '..') {
      segments.pop()
      continue
    }

    segments.push(segment)
  }

  return segments.join('/')
}

function documentOf(entries: ZipEntries, path: string): Document | null {
  const bytes = entries.get(path)

  return bytes ? parseDocument(decodeXml(bytes)) : null
}

function textOf(element: Element | null): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim()
}

function headingOf(doc: Document): string {
  for (const element of elements(doc)) {
    if (/^h[1-6]$/.test(nameOf(element))) {
      const text = textOf(element)

      if (text !== '') {
        return text
      }
    }
  }

  return ''
}

function isApparatus(doc: Document): boolean {
  return [doc.documentElement, bodyOf(doc)].some((element) =>
    epubType(element).some((type) => APPARATUS_TYPES.has(type)),
  )
}

/** A TOC entry: a label, and the fragment id it points at inside its document ('' for none). */
interface TocEntry {
  fragment: string
  label: string
}

type TocEntries = Map<string, TocEntry[]>

/**
 * TOC entries by document path, in TOC order. An epub 3 navigation document first, an epub 2
 * toc.ncx second. Hrefs resolve against the TOC's own directory, which is not always the package's.
 */
function tocEntries(entries: ZipEntries, opf: Document, manifest: Map<string, ManifestItem>): TocEntries {
  const nav = [...manifest.values()].find((item) => item.properties.includes('nav'))

  if (nav) {
    const titles = navEntries(entries, nav.path)

    if (titles.size > 0) {
      return titles
    }
  }

  const tocId = firstByName(opf, 'spine')?.getAttribute('toc')
  const ncx =
    (tocId ? manifest.get(tocId) : undefined) ??
    [...manifest.values()].find((item) => item.mediaType === 'application/x-dtbncx+xml')

  return ncx ? ncxEntries(entries, ncx.path) : new Map()
}

function navEntries(entries: ZipEntries, path: string): TocEntries {
  const titles: TocEntries = new Map()
  const doc = documentOf(entries, path)

  if (!doc) {
    return titles
  }

  const navs = allByName(doc, 'nav')
  const toc = navs.find((nav) => epubType(nav).includes('toc')) ?? navs[0]

  if (!toc) {
    return titles
  }

  for (const link of allByName(toc, 'a')) {
    const href = link.getAttribute('href')
    const label = textOf(link)

    if (href && label !== '') {
      remember(titles, dirOf(path), href, label)
    }
  }

  return titles
}

function ncxEntries(entries: ZipEntries, path: string): TocEntries {
  const titles: TocEntries = new Map()
  const doc = documentOf(entries, path)

  if (!doc) {
    return titles
  }

  // nameOf() lowercases every localName, so the element name to match against must be lowercase too.
  for (const point of allByName(doc, 'navpoint')) {
    const src = firstByName(point, 'content')?.getAttribute('src')
    const label = textOf(firstByName(point, 'text'))

    if (src && label !== '') {
      remember(titles, dirOf(path), src, label)
    }
  }

  return titles
}

/** The first entry for a place wins: a second entry pointing at the same spot must not rename it. */
function remember(titles: TocEntries, base: string, href: string, label: string) {
  const path = resolvePath(base, href)
  const hash = href.indexOf('#')
  const fragment = hash < 0 ? '' : percentDecoded(href.slice(hash + 1))
  const known = titles.get(path) ?? []

  if (!known.some((entry) => entry.fragment === fragment)) {
    titles.set(path, [...known, { fragment, label }])
  }
}

/**
 * One spine document → its chapters. Every TOC fragment found in the document starts a chapter
 * named by its label; what comes before the first of them is named by the document's other TOC
 * entry (a bare href, or a fragment the document does not have), and when the TOC cuts nothing,
 * by its first heading. A chapter left with no text — a part title right before its first
 * chapter — is dropped.
 */
function chaptersOf(doc: Document, toc: TocEntry[]): EpubDocument[] {
  const labels = new Map<string, string>()

  for (const entry of toc) {
    if (entry.fragment !== '') {
      labels.set(entry.fragment, entry.label)
    }
  }

  const blocks = blocksOf(doc, new Set(labels.keys()))
  const found = new Set(blocks.flatMap((block) => ('cut' in block ? [block.cut] : [])))
  const opening = toc.find((entry) => !found.has(entry.fragment))?.label
  let current: EpubDocument = { title: opening ?? (found.size === 0 ? headingOf(doc) : ''), paragraphs: [] }
  const chapters = [current]

  for (const block of blocks) {
    if ('cut' in block) {
      current = { title: labels.get(block.cut) ?? '', paragraphs: [] }
      chapters.push(current)
    } else {
      current.paragraphs.push(block.text)
    }
  }

  return chapters.filter((chapter) => chapter.paragraphs.length > 0)
}

function readManifest(opf: Document, base: string): Map<string, ManifestItem> {
  const items = new Map<string, ManifestItem>()

  for (const item of allByName(firstByName(opf, 'manifest') ?? opf, 'item')) {
    const id = item.getAttribute('id')
    const href = item.getAttribute('href')

    if (!id || !href) {
      continue
    }

    items.set(id, {
      path: resolvePath(base, href),
      mediaType: (item.getAttribute('media-type') ?? '').toLowerCase(),
      properties: (item.getAttribute('properties') ?? '').toLowerCase().split(/\s+/).filter(Boolean),
    })
  }

  return items
}

/**
 * An epub → the book. Chapters are the spine's XHTML documents in the spine's own order, each cut
 * further wherever the TOC points inside it (`chaptersOf`); the
 * navigation document, apparatus, non-XHTML items, `linear="no"` items, items whose file or
 * manifest entry is missing, and a document listed a second time are all left out.
 */
export function parseEpub(entries: ZipEntries, fallbackTitle: string): EpubBook {
  const container = documentOf(entries, 'META-INF/container.xml')
  const opfPath = container ? firstByName(container, 'rootfile')?.getAttribute('full-path') : null
  const opf = opfPath ? documentOf(entries, resolvePath('', opfPath)) : null

  if (!opfPath || !opf) {
    throw new BookFormatError('invalid')
  }

  const base = dirOf(resolvePath('', opfPath))
  const metadata = firstByName(opf, 'metadata')
  const manifest = readManifest(opf, base)
  const toc = tocEntries(entries, opf, manifest)
  const spine = firstByName(opf, 'spine')
  const seen = new Set<string>()
  const docs: EpubDocument[] = []

  for (const ref of spine ? allByName(spine, 'itemref') : []) {
    if (ref.getAttribute('linear') === 'no') {
      continue
    }

    const item = manifest.get(ref.getAttribute('idref') ?? '')

    if (!item || !XHTML_TYPES.has(item.mediaType) || item.properties.includes('nav') || seen.has(item.path)) {
      continue
    }

    seen.add(item.path)

    const doc = documentOf(entries, item.path)

    if (!doc || isApparatus(doc)) {
      continue
    }

    docs.push(...chaptersOf(doc, toc.get(item.path) ?? []))
  }

  if (docs.length === 0) {
    throw new BookFormatError('invalid')
  }

  return {
    title: (metadata ? textOf(firstByName(metadata, 'title')) : '') || fallbackTitle,
    author: metadata ? textOf(firstByName(metadata, 'creator')) : '',
    docs,
  }
}
