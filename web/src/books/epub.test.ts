import { describe, expect, it } from 'vitest'
import { CONTAINER_XML, EPUB3_FILES, epub3Bytes, xhtmlDoc, zipBytes } from '../test/epubFixtures'
import { documentParagraphs, parseEpub } from './epub'
import { BookFormatError } from './formatError'
import { readZip } from './zip'

function xhtml(body: string): string {
  return `<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>x</title></head><body>${body}</body></html>`
}

describe('documentParagraphs', () => {
  it('takes one paragraph per block element, in document order', () => {
    const paragraphs = documentParagraphs(
      xhtml('<p>The silo was quiet.</p><blockquote><p>Holston climbed.</p></blockquote><ul><li>A list line.</li></ul>'),
    )

    expect(paragraphs).toEqual(['The silo was quiet.', 'Holston climbed.', 'A list line.'])
  })

  it('leaves out headings, scripts, navigation and footnotes', () => {
    const paragraphs = documentParagraphs(
      xhtml(
        '<h1>Chapter One</h1><p>Real text.</p><script>var a = 1;</script><style>p{color:red}</style>' +
          '<nav epub:type="toc"><ol><li><a href="ch01.xhtml">Chapter One</a></li></ol></nav>' +
          '<aside epub:type="footnote"><p>A footnote.</p></aside>',
      ),
    )

    expect(paragraphs).toEqual(['Real text.'])
  })

  it('collapses whitespace and drops empty paragraphs', () => {
    expect(documentParagraphs(xhtml('<p>  The   silo\n was quiet. </p><p> </p><p></p>'))).toEqual(['The silo was quiet.'])
  })

  it('falls back to text-bearing elements in a book whose paragraphs are divs', () => {
    const paragraphs = documentParagraphs(xhtml('<div class="body"><div>First line.</div><div>Second line.</div></div>'))

    expect(paragraphs).toEqual(['First line.', 'Second line.'])
  })

  it('reads a document that is HTML rather than well-formed XML', () => {
    const html = '<html><body><p>Unclosed paragraph.<br>Still text.</body></html>'

    expect(documentParagraphs(html)).toEqual(['Unclosed paragraph. Still text.'])
  })

  it('yields nothing for a document with no text at all', () => {
    expect(documentParagraphs(xhtml('<div><img src="cover.png"/></div>'))).toEqual([])
  })

  // A <br> or a run of nested paragraph tags with no whitespace between their closing and
  // opening angle brackets (common in minified or generated XHTML) must not glue two words
  // together into one unreadable, undetectable token.
  it('inserts a separator at <br> and between nested blocks, instead of gluing words together', () => {
    const paragraphs = documentParagraphs(
      xhtml('<blockquote><p>He climbed.</p><p>She fell.</p></blockquote><p>The wind<br/>blew hard.</p>'),
    )

    expect(paragraphs).toEqual(['He climbed. She fell.', 'The wind blew hard.'])
  })

  it('does not let a nested script or heading leak into a paragraph it sits inside', () => {
    const paragraphs = documentParagraphs(xhtml('<p>Before.<script>var a = 1;</script>After.</p>'))

    expect(paragraphs).toEqual(['Before.After.'])
  })
})

const book = parseEpub(readZip(epub3Bytes()), 'fallback')

describe('parseEpub', () => {
  it('takes the title and the author from the package metadata', () => {
    expect(book.title).toBe("Death's End")
    expect(book.author).toBe('Cixin Liu')
  })

  it('falls back to the file name when the package names no title', () => {
    const opf = (EPUB3_FILES['OEBPS/content.opf'] as string).replace("<dc:title>Death's End</dc:title>", '')

    expect(parseEpub(readZip(epub3Bytes({ 'OEBPS/content.opf': opf })), 'deaths-end').title).toBe('deaths-end')
  })

  it('makes a chapter of every readable spine document, in spine order', () => {
    // cover: linear="no"; nav: the navigation document; notes: epub:type="footnotes";
    // picture: not XHTML; gone: its file is not in the archive; ghost: no manifest item;
    // and ch01 is listed twice.
    expect(book.docs.map((doc) => doc.paragraphs[0])).toEqual([
      'Compared to the beginning, fewer individuals were emerging.',
      'Most men tried to adjust.',
      'The droplet came at noon.',
    ])
  })

  // Review Focus 1: a repeated spine entry would double the chapter and its word counts.
  it('keeps a document listed twice in the spine only once', () => {
    expect(book.docs.filter((doc) => doc.paragraphs[0].startsWith('Compared to the beginning'))).toHaveLength(1)
  })

  // Review Focus 2: a dangling idref and a manifest item with no file must not crash the parse.
  it('skips a spine item with no manifest entry and one whose file is missing', () => {
    expect(book.docs).toHaveLength(3)
  })

  it('resolves hrefs against the package directory, percent-decoded and with .. collapsed', () => {
    expect(book.docs[1].paragraphs).toEqual(['Most men tried to adjust.'])
    expect(book.docs[2].paragraphs).toEqual(['The droplet came at noon.'])
  })

  it('names a chapter by its first heading when the TOC does not', () => {
    expect(book.docs[2].title).toBe('Chapter Three')
  })

  it('keeps every paragraph of a chapter', () => {
    expect(book.docs[0].paragraphs).toEqual([
      'Compared to the beginning, fewer individuals were emerging.',
      'They still formed a stratum.',
    ])
  })

  it('refuses an archive with no container, no package or no text', () => {
    expect(() => parseEpub(readZip(zipBytes({ mimetype: 'application/epub+zip' })), 'x')).toThrow(BookFormatError)
    expect(() => parseEpub(readZip(zipBytes({ 'META-INF/container.xml': CONTAINER_XML })), 'x')).toThrow(
      "This file isn't a readable fb2 or epub book.",
    )
    expect(() =>
      parseEpub(readZip(epub3Bytes({ 'OEBPS/text/ch01.xhtml': xhtmlDoc('<div><img src="x.png"/></div>'), 'OEBPS/text/ch 02.xhtml': xhtmlDoc('<p> </p>'), 'OEBPS/text/ch03.xhtml': xhtmlDoc('') })), 'x'),
    ).toThrow(BookFormatError)
  })

  it('names chapters from an epub 3 navigation document, the first entry winning per file', () => {
    // ch01 also has an <h1>1</h1>; the TOC label must win. Its second TOC entry
    // ("The Swordholder, later") points at the same file and must not rename it.
    expect(book.docs.map((doc) => doc.title)).toEqual(['The Swordholder', 'Year 62', 'Chapter Three'])
  })

  it('names chapters from an epub 2 toc.ncx when there is no navigation document', () => {
    const ncx = `<?xml version="1.0" encoding="utf-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
  <navMap>
    <navPoint id="p1" playOrder="1">
      <navLabel><text>The Swordholder</text></navLabel>
      <content src="text/ch01.xhtml"/>
      <navPoint id="p1a" playOrder="2">
        <navLabel><text>Later that day</text></navLabel>
        <content src="text/ch01.xhtml#later"/>
      </navPoint>
    </navPoint>
    <navPoint id="p2" playOrder="3">
      <navLabel><text>Year 62</text></navLabel>
      <content src="text/ch%2002.xhtml"/>
    </navPoint>
  </navMap>
</ncx>`
    const opf = (EPUB3_FILES['OEBPS/content.opf'] as string)
      .replace('<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>', '<item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>')
      .replace('<spine>', '<spine toc="ncx">')
      .replace('<itemref idref="nav"/>', '')

    const epub2 = parseEpub(readZip(epub3Bytes({ 'OEBPS/content.opf': opf, 'OEBPS/toc.ncx': ncx })), 'x')

    expect(epub2.docs.map((doc) => doc.title)).toEqual(['The Swordholder', 'Year 62', 'Chapter Three'])
  })

  it('resolves TOC hrefs against the TOC document, not the package', () => {
    // The nav document moves one level down, so its hrefs lose the "text/" prefix.
    const nav = xhtmlDoc('<nav epub:type="toc"><ol><li><a href="ch01.xhtml">The Swordholder</a></li></ol></nav>')
    const opf = (EPUB3_FILES['OEBPS/content.opf'] as string).replace('href="nav.xhtml"', 'href="text/nav.xhtml"')
    const moved = parseEpub(readZip(epub3Bytes({ 'OEBPS/content.opf': opf, 'OEBPS/text/nav.xhtml': nav })), 'x')

    expect(moved.docs[0].title).toBe('The Swordholder')
  })
})
