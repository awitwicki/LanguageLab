/**
 * A small fb2 in the shape real ones have: a default namespace, nested sections, a part title
 * with no text of its own, a poem, an empty line and a notes body.
 * Chapters the reader must see: "The Swordholder" (2 paragraphs, 3 sentences) and "Year 62"
 * (3 paragraphs: two poem lines and one prose line).
 */
export const READER_BOOK_XML = `<?xml version="1.0" encoding="utf-8"?>
<FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
  <description>
    <title-info>
      <author><first-name>Cixin</first-name><last-name>Liu</last-name></author>
      <book-title>Death's End</book-title>
    </title-info>
  </description>
  <body>
    <section>
      <title><p>Part One</p></title>
      <section>
        <title><p>The Swordholder</p></title>
        <p>Compared to the beginning, fewer individuals were emerging. They still formed a stratum.</p>
        <p>All of them had some difficulty reintegrating.</p>
      </section>
      <section>
        <title><p>Year 62</p></title>
        <poem><stanza><v>The silo was quiet,</v><v>the silo was cold.</v></stanza></poem>
        <empty-line/>
        <p>Most men tried to adjust.</p>
      </section>
    </section>
  </body>
  <body name="notes">
    <section><title><p>Notes</p></title><p>A footnote.</p></section>
  </body>
</FictionBook>`

export function bytesOf(text: string): ArrayBuffer {
  return new TextEncoder().encode(text).buffer as ArrayBuffer
}
