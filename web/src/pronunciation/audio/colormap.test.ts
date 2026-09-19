import { describe, expect, it } from 'vitest'
import { buildLut, parseColor } from './colormap'

describe('parseColor', () => {
  it('reads #rrggbb, #rgb, rgb() and rgba()', () => {
    expect(parseColor('#0071e3')).toEqual([0, 113, 227])
    expect(parseColor('#fff')).toEqual([255, 255, 255])
    expect(parseColor('rgb(29, 29, 31)')).toEqual([29, 29, 31])
    expect(parseColor('rgba(0, 0, 0, 0.08)')).toEqual([0, 0, 0])
  })

  it('returns null for anything else', () => {
    expect(parseColor('')).toBeNull()
    expect(parseColor('transparent')).toBeNull()
  })
})

describe('buildLut', () => {
  it('interpolates 256 entries through the stops', () => {
    const lut = buildLut([
      [0, 0, 0],
      [100, 100, 100],
      [200, 200, 200],
    ])

    expect(lut).toHaveLength(768)
    expect([lut[0], lut[1], lut[2]]).toEqual([0, 0, 0])
    expect([lut[128 * 3], lut[128 * 3 + 1], lut[128 * 3 + 2]]).toEqual([100, 100, 100])
    expect([lut[255 * 3], lut[255 * 3 + 1], lut[255 * 3 + 2]]).toEqual([200, 200, 200])
  })
})
