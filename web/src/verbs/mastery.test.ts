import { describe, expect, it } from 'vitest'
import { masteryBand, masteryPercent } from './mastery'

describe('masteryBand', () => {
  it('leaves a verb with no answers unpainted', () => {
    expect(masteryBand(0, 0)).toBe('none')
    // A stored mastery cannot be high with no answers behind it, but the rule must not care.
    expect(masteryBand(0.95, 0)).toBe('none')
  })

  it('paints each band at its own boundary', () => {
    expect(masteryBand(0, 1)).toBe('low')
    expect(masteryBand(0.34, 1)).toBe('low')
    expect(masteryBand(0.35, 1)).toBe('mid')
    expect(masteryBand(0.59, 1)).toBe('mid')
    expect(masteryBand(0.6, 1)).toBe('high')
    expect(masteryBand(0.79, 1)).toBe('high')
    expect(masteryBand(0.8, 1)).toBe('top')
    expect(masteryBand(1, 1)).toBe('top')
  })
})

describe('masteryPercent', () => {
  it('rounds to whole percent', () => {
    expect(masteryPercent(0)).toBe(0)
    expect(masteryPercent(0.725)).toBe(73)
    expect(masteryPercent(1)).toBe(100)
  })
})
