import { describe, expect, it } from 'vitest'
import { flattenChapters, parseBook } from './chapters'

const NESTED = `<?xml version="1.0" encoding="utf-8"?>
<FictionBook>
  <description><title-info><book-title>Wool</book-title></title-info></description>
  <body>
    <section>
      <title><p>Part One</p></title>
      <section><title><p>Chapter 1</p></title><p>the children were playing</p></section>
      <section><title><p>Chapter 2</p></title><p>holston climbed</p></section>
    </section>
  </body>
  <body name="notes">
    <section><title><p>Notes</p></title><p>footnote text</p></section>
  </body>
  <binary id="cover" content-type="image/jpeg">AAAABBBBCCCC</binary>
</FictionBook>`

const WITH_PROLOGUE = `<?xml version="1.0" encoding="utf-8"?>
<FictionBook>
  <description><title-info><book-title>Wool</book-title></title-info></description>
  <body>
    <section><title><p>Prologue</p></title><p>before the beginning</p></section>
    <section>
      <title><p>Part One</p></title>
      <section><title><p>Chapter 1</p></title><p>the children were playing</p></section>
      <section><title><p>Chapter 2</p></title><p>holston climbed</p></section>
    </section>
  </body>
</FictionBook>`

describe('parseBook', () => {
  it('takes the dictionary name from book-title', () => {
    expect(parseBook(NESTED).bookTitle).toBe('Wool')
  })

  it('reports the deepest section level', () => {
    expect(parseBook(NESTED).maxDepth).toBe(2)
  })

  it('ignores the notes body', () => {
    const flat = JSON.stringify(parseBook(NESTED).sections)

    expect(flat).not.toContain('footnote text')
  })

  it('ignores binary payloads', () => {
    const flat = JSON.stringify(parseBook(NESTED).sections)

    expect(flat).not.toContain('AAAABBBBCCCC')
  })
})

describe('flattenChapters', () => {
  it('treats leaf sections as chapters by default', () => {
    const chapters = flattenChapters(parseBook(NESTED).sections, 'leaf')

    expect(chapters.map((c) => c.title)).toEqual(['Chapter 1', 'Chapter 2'])
    expect(chapters[0].text).toContain('the children were playing')
  })

  it('collapses deeper sections when a depth is given', () => {
    const chapters = flattenChapters(parseBook(NESTED).sections, 1)

    expect(chapters.map((c) => c.title)).toEqual(['Part One'])
    expect(chapters[0].text).toContain('the children were playing')
    expect(chapters[0].text).toContain('holston climbed')
  })

  it('keeps untitled sections with an empty title', () => {
    const xml = `<FictionBook><body><section><p>no title here</p></section></body></FictionBook>`
    const chapters = flattenChapters(parseBook(xml).sections, 'leaf')

    expect(chapters).toHaveLength(1)
    expect(chapters[0].title).toBe('')
  })

  it('keeps a childless section above the requested depth instead of dropping it', () => {
    // Prologue sits at depth 1 with no children, next to a Part One (depth 1)
    // that branches into Chapter 1/Chapter 2 (depth 2). At mode: 2, Prologue
    // is shallower than the requested depth and has no children to recurse
    // into — it must still surface as its own chapter, not vanish silently.
    const chapters = flattenChapters(parseBook(WITH_PROLOGUE).sections, 2)

    expect(chapters.map((c) => c.title)).toEqual(['Prologue', 'Chapter 1', 'Chapter 2'])
    expect(chapters[0].text).toContain('before the beginning')
  })
})
