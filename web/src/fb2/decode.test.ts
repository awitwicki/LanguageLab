import { describe, expect, it } from 'vitest'
import { decodeFb2 } from './decode'

function buffer(bytes: number[]): ArrayBuffer {
  return new Uint8Array(bytes).buffer
}

describe('decodeFb2', () => {
  it('reads utf-8 when the prolog says so', () => {
    const text = '<?xml version="1.0" encoding="utf-8"?><FictionBook>тест</FictionBook>'
    const bytes = new TextEncoder().encode(text)

    expect(decodeFb2(bytes.buffer)).toContain('тест')
  })

  it('reads windows-1251 declared in the prolog', () => {
    // "<?xml version='1.0' encoding='windows-1251'?><b>Ц</b>" — Ц у cp1251 це 0xD6,
    // а в utf-8 той самий байт дав би заміну U+FFFD.
    const prolog = "<?xml version='1.0' encoding='windows-1251'?><b>"
    const bytes = [...prolog].map((c) => c.charCodeAt(0))
    bytes.push(0xd6)
    bytes.push(...[...'</b>'].map((c) => c.charCodeAt(0)))

    expect(decodeFb2(buffer(bytes))).toContain('Ц')
  })

  it('falls back to utf-8 when the declared encoding is unknown', () => {
    const text = '<?xml version="1.0" encoding="totally-made-up"?><FictionBook>ok</FictionBook>'
    const bytes = new TextEncoder().encode(text)

    expect(decodeFb2(bytes.buffer)).toContain('ok')
  })

  it('defaults to utf-8 when there is no prolog', () => {
    const bytes = new TextEncoder().encode('<FictionBook>ok</FictionBook>')

    expect(decodeFb2(bytes.buffer)).toContain('ok')
  })
})
