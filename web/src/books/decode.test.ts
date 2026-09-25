import { describe, expect, it } from 'vitest'
import { decodeXml } from './decode'

function buffer(bytes: number[]): ArrayBuffer {
  return new Uint8Array(bytes).buffer
}

describe('decodeXml', () => {
  it('reads utf-8 when the prolog says so', () => {
    const text = '<?xml version="1.0" encoding="utf-8"?><FictionBook>тест</FictionBook>'

    expect(decodeXml(new TextEncoder().encode(text).buffer as ArrayBuffer)).toContain('тест')
  })

  it('reads windows-1251 declared in the prolog', () => {
    // "<?xml version='1.0' encoding='windows-1251'?><b>Ц</b>" — Ц is 0xD6 in cp1251,
    // while in utf-8 the same byte would yield the U+FFFD replacement.
    const prolog = "<?xml version='1.0' encoding='windows-1251'?><b>"
    const bytes = [...prolog].map((c) => c.charCodeAt(0))
    bytes.push(0xd6)
    bytes.push(...[...'</b>'].map((c) => c.charCodeAt(0)))

    expect(decodeXml(buffer(bytes))).toContain('Ц')
  })

  it('falls back to utf-8 when the declared encoding is unknown', () => {
    const text = '<?xml version="1.0" encoding="totally-made-up"?><FictionBook>ok</FictionBook>'

    expect(decodeXml(new TextEncoder().encode(text).buffer as ArrayBuffer)).toContain('ok')
  })

  it('defaults to utf-8 when there is no prolog', () => {
    expect(decodeXml(new TextEncoder().encode('<FictionBook>ok</FictionBook>').buffer as ArrayBuffer)).toContain('ok')
  })

  it('takes a Uint8Array, the way a zip entry arrives', () => {
    expect(decodeXml(new TextEncoder().encode('<p>ok</p>'))).toBe('<p>ok</p>')
  })

  it('honours a charset declared by an XHTML meta element', () => {
    const head = '<html><head><meta charset="windows-1251"/></head><body><p>'
    const bytes = [...head].map((c) => c.charCodeAt(0))
    bytes.push(0xd6)
    bytes.push(...[...'</p></body></html>'].map((c) => c.charCodeAt(0)))

    expect(decodeXml(buffer(bytes))).toContain('Ц')
  })
})
