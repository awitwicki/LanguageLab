export type Rgb = [number, number, number]

export function parseColor(css: string): Rgb | null {
  const value = css.trim()
  const hex = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value)
  if (hex) {
    const digits = hex[1].length === 3 ? [...hex[1]].map((d) => d + d).join('') : hex[1]
    return [parseInt(digits.slice(0, 2), 16), parseInt(digits.slice(2, 4), 16), parseInt(digits.slice(4, 6), 16)]
  }
  const rgb = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i.exec(value)
  if (rgb) return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])]
  return null
}

/** 256 RGB triples, intensity 0 → first stop, 255 → last stop, linear in sRGB between stops. */
export function buildLut(stops: Rgb[]): Uint8ClampedArray {
  const lut = new Uint8ClampedArray(256 * 3)
  const segments = stops.length - 1
  for (let i = 0; i < 256; i++) {
    const position = (i / 255) * segments
    const index = Math.min(segments - 1, Math.floor(position))
    const t = position - index
    const from = stops[index]
    const to = stops[index + 1]
    for (let c = 0; c < 3; c++) lut[i * 3 + c] = Math.round(from[c] + (to[c] - from[c]) * t)
  }
  return lut
}
