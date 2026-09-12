import { describe, expect, it } from 'vitest'
import { familyPattern, stateLabel, triplet } from './format'

describe('triplet', () => {
  it('joins the three forms with en dashes', () => {
    expect(triplet('go', 'went', 'gone')).toBe('go – went – gone')
  })
})

describe('stateLabel', () => {
  it('labels every learning state', () => {
    expect(stateLabel('new')).toBe('Not started')
    expect(stateLabel('learning1')).toBe('Learning')
    expect(stateLabel('learning2')).toBe('Learning')
    expect(stateLabel('learning3')).toBe('Learning')
    expect(stateLabel('learned')).toBe('Learned')
    expect(stateLabel('mastered')).toBe('Learned')
    expect(stateLabel('forgotten')).toBe('Forgotten')
  })
})

describe('familyPattern', () => {
  it('joins suffixes with a slash, or falls back when there are none', () => {
    expect(familyPattern(['ought', 'aught'])).toBe('-ought / -aught')
    expect(familyPattern([])).toBe('')
  })
})
