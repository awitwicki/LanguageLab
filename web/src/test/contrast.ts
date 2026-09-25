/**
 * Test-only color math for the design tokens: reads what index.css and a component's CSS
 * actually declare — hex, `var()`, `color-mix(in srgb, …)`, `transparent` — and scores a
 * pair the way WCAG 2.1 does. Nothing outside tests imports it, so it never ships.
 *
 * Deliberately narrow: it understands the color syntax this project's CSS uses and throws
 * on anything else rather than guessing.
 */

export interface Rgba {
  r: number
  g: number
  b: number
  a: number
}

export type Tokens = Record<string, string>

/** The body of `<selector> { … }`, which is flat everywhere this is used. */
function blockOf(css: string, selector: string, from = 0): string {
  const head = css.indexOf(`${selector} {`, from)
  if (head === -1) {
    throw new Error(`No rule for "${selector}"`)
  }

  const start = head + `${selector} {`.length
  const end = css.indexOf('}', start)
  return css.slice(start, end)
}

/** Every custom property declared in `<selector> { … }`. */
export function tokensOf(css: string, selector: string, from = 0): Tokens {
  const tokens: Tokens = {}
  for (const [, name, value] of blockOf(css, selector, from).matchAll(/(--[\w-]+):\s*([^;]+);/g)) {
    tokens[name] = value.trim()
  }
  return tokens
}

/** The light tokens with the `prefers-color-scheme: dark` overrides applied on top. */
export function darkTokensOf(css: string): Tokens {
  const media = css.indexOf('@media (prefers-color-scheme: dark)')
  if (media === -1) {
    throw new Error('No dark-theme block')
  }

  return { ...tokensOf(css, ':root'), ...tokensOf(css, ':root', media) }
}

export function declarationOf(css: string, selector: string, property: string): string {
  const match = new RegExp(`(?:^|;)\\s*${property}:\\s*([^;]+);`).exec(blockOf(css, selector))
  if (!match) {
    throw new Error(`No "${property}" in "${selector}"`)
  }
  return match[1].trim()
}

/** Splits on the commas that are not inside nested parentheses. */
function argumentsOf(value: string): string[] {
  const parts: string[] = []
  let depth = 0
  let current = ''

  for (const character of value) {
    if (character === '(') depth++
    if (character === ')') depth--
    if (character === ',' && depth === 0) {
      parts.push(current.trim())
      current = ''
    } else {
      current += character
    }
  }

  parts.push(current.trim())
  return parts
}

export function resolveColor(value: string, tokens: Tokens): Rgba {
  const text = value.trim()

  if (text === 'transparent') {
    return { r: 0, g: 0, b: 0, a: 0 }
  }

  const hex = /^#([0-9a-f]{6})$/i.exec(text)
  if (hex) {
    const n = parseInt(hex[1], 16)
    return { r: (n >> 16) & 0xff, g: (n >> 8) & 0xff, b: n & 0xff, a: 1 }
  }

  const variable = /^var\((--[\w-]+)\)$/.exec(text)
  if (variable) {
    const token = tokens[variable[1]]
    if (token === undefined) {
      throw new Error(`Undefined token "${variable[1]}"`)
    }
    return resolveColor(token, tokens)
  }

  if (text.startsWith('color-mix(')) {
    const [space, first, second] = argumentsOf(text.slice('color-mix('.length, -1))
    if (space.trim() !== 'in srgb') {
      throw new Error(`Only "in srgb" is supported, got "${space.trim()}"`)
    }

    const share = /^(.*?)\s+([\d.]+)%$/.exec(first)
    if (!share) {
      throw new Error(`No percentage in "${first}"`)
    }

    return mix(resolveColor(share[1], tokens), Number(share[2]) / 100, resolveColor(second, tokens))
  }

  throw new Error(`Unsupported color "${text}"`)
}

/** color-mix in sRGB: gamma-encoded channels, interpolated premultiplied by alpha. */
function mix(first: Rgba, share: number, second: Rgba): Rgba {
  const a = first.a * share + second.a * (1 - share)
  const channel = (key: 'r' | 'g' | 'b') =>
    a === 0 ? 0 : (first[key] * first.a * share + second[key] * second.a * (1 - share)) / a

  return { r: channel('r'), g: channel('g'), b: channel('b'), a }
}

/** Composites a translucent fill over an opaque background. */
export function over(fill: Rgba, background: Rgba): Rgba {
  const channel = (key: 'r' | 'g' | 'b') => fill[key] * fill.a + background[key] * (1 - fill.a)
  return { r: channel('r'), g: channel('g'), b: channel('b'), a: 1 }
}

function luminance({ r, g, b }: Rgba): number {
  const linear = (value: number) => {
    const channel = value / 255
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b)
}

export function contrastRatio(first: Rgba, second: Rgba): number {
  const [a, b] = [luminance(first), luminance(second)].sort((x, y) => y - x)
  return (a + 0.05) / (b + 0.05)
}
