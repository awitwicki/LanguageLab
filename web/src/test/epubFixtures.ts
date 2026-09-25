import { strToU8, zipSync } from 'fflate'

/** Files → a zip, the way the reader receives one. */
export function zipBytes(files: Record<string, string | Uint8Array>): ArrayBuffer {
  const entries: Record<string, Uint8Array> = {}

  for (const [path, value] of Object.entries(files)) {
    entries[path] = typeof value === 'string' ? strToU8(value) : value
  }

  return zipSync(entries).slice().buffer as ArrayBuffer
}

export function xhtmlDoc(body: string, attributes = ''): string {
  return `<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>x</title></head><body${attributes}>${body}</body></html>`
}

export const CONTAINER_XML = `<?xml version="1.0"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
</container>`

const OPF = `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:title>Death's End</dc:title>
    <dc:creator>Cixin Liu</dc:creator>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="cover" href="cover.xhtml" media-type="application/xhtml+xml"/>
    <item id="c1" href="text/ch01.xhtml" media-type="application/xhtml+xml"/>
    <item id="c2" href="text/ch%2002.xhtml" media-type="application/xhtml+xml"/>
    <item id="c3" href="text/../text/ch03.xhtml" media-type="application/xhtml+xml"/>
    <item id="gone" href="text/ch04.xhtml" media-type="application/xhtml+xml"/>
    <item id="notes" href="text/notes.xhtml" media-type="application/xhtml+xml"/>
    <item id="picture" href="images/cover.png" media-type="image/png"/>
  </manifest>
  <spine>
    <itemref idref="cover" linear="no"/>
    <itemref idref="nav"/>
    <itemref idref="c1"/>
    <itemref idref="c2"/>
    <itemref idref="c3"/>
    <itemref idref="gone"/>
    <itemref idref="notes"/>
    <itemref idref="picture"/>
    <itemref idref="ghost"/>
    <itemref idref="c1"/>
  </spine>
</package>`

const NAV = xhtmlDoc(
  `<nav epub:type="toc"><ol>
    <li><a href="text/ch01.xhtml">The Swordholder</a></li>
    <li><a href="text/ch01.xhtml#later">The Swordholder, later</a></li>
    <li><a href="text/ch%2002.xhtml#start">Year 62</a></li>
  </ol></nav>`,
)

/** The whole epub 3 book. Spread it and override a path to build a variant. */
export const EPUB3_FILES: Record<string, string | Uint8Array> = {
  mimetype: 'application/epub+zip',
  'META-INF/container.xml': CONTAINER_XML,
  'OEBPS/content.opf': OPF,
  'OEBPS/nav.xhtml': NAV,
  'OEBPS/cover.xhtml': xhtmlDoc('<div><img src="images/cover.png"/></div>'),
  'OEBPS/text/ch01.xhtml': xhtmlDoc('<h1>1</h1><p>Compared to the beginning, fewer individuals were emerging.</p><p>They still formed a stratum.</p>'),
  'OEBPS/text/ch 02.xhtml': xhtmlDoc('<p>Most men tried to adjust.</p>'),
  'OEBPS/text/ch03.xhtml': xhtmlDoc('<h2>Chapter Three</h2><p>The droplet came at noon.</p>'),
  'OEBPS/text/notes.xhtml': xhtmlDoc('<p>A footnote.</p>', ' epub:type="footnotes"'),
  'OEBPS/images/cover.png': new Uint8Array([0x89, 0x50, 0x4e, 0x47]),
}

export function epub3Bytes(overrides: Record<string, string | Uint8Array> = {}): ArrayBuffer {
  return zipBytes({ ...EPUB3_FILES, ...overrides })
}
