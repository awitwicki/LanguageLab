import { strToU8, zipSync } from 'fflate'
import { describe, expect, it } from 'vitest'
import { looksZipped, readZip } from './zip'

function zipped(files: Record<string, Uint8Array>): ArrayBuffer {
  return zipSync(files).slice().buffer as ArrayBuffer
}

describe('looksZipped', () => {
  it('recognises a zip by its signature and nothing else', () => {
    expect(looksZipped(zipped({ 'a.txt': strToU8('a') }))).toBe(true)
    expect(looksZipped(new TextEncoder().encode('<FictionBook/>').buffer as ArrayBuffer)).toBe(false)
    expect(looksZipped(new ArrayBuffer(0))).toBe(false)
  })
})

describe('readZip', () => {
  it('reads stored and deflated entries alike', () => {
    // level 0 stores the bytes as they are, level 9 deflates them; an epub has both
    // (its "mimetype" entry must be stored) and the reader must not care which.
    const bytes = zipSync(
      { mimetype: [strToU8('application/epub+zip'), { level: 0 }], 'ch01.xhtml': [strToU8('<p>Hello there.</p>'), { level: 9 }] },
    ).slice().buffer as ArrayBuffer

    const entries = readZip(bytes)

    expect(new TextDecoder().decode(entries.get('mimetype'))).toBe('application/epub+zip')
    expect(new TextDecoder().decode(entries.get('ch01.xhtml'))).toBe('<p>Hello there.</p>')
  })

  it('drops directory entries and the junk a Mac adds', () => {
    const entries = readZip(
      zipped({ 'book/': strToU8(''), 'book/ch01.xhtml': strToU8('x'), '__MACOSX/._ch01.xhtml': strToU8('junk'), '.DS_Store': strToU8('junk') }),
    )

    expect([...entries.keys()]).toEqual(['book/ch01.xhtml'])
  })

  it('throws on a corrupt archive', () => {
    const bytes = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x00, 0x01, 0x02]).buffer

    expect(() => readZip(bytes)).toThrow()
  })

  // Review Focus 5: a real book is megabytes over hundreds of entries, all unzipped at once.
  it('reads a book-sized archive whole', () => {
    const paragraph = 'The silo was quiet and the stairs were long. '.repeat(600)
    const files: Record<string, Uint8Array> = {}

    for (let i = 0; i < 300; i++) {
      files[`text/ch${i}.xhtml`] = strToU8(`<p>${i} ${paragraph}</p>`)
    }

    const entries = readZip(zipped(files))

    expect(entries.size).toBe(300)
    expect(new TextDecoder().decode(entries.get('text/ch299.xhtml'))).toContain('299 The silo was quiet')
  })
})
