import { describe, expect, it } from 'vitest'
import { modeOf } from './mode'

describe('modeOf', () => {
  it('puts every book and personal-dictionary screen under Words', () => {
    for (const name of ['home', 'import', 'dictionary', 'personal', 'sorting', 'training-start', 'training']) {
      expect(modeOf(name)).toBe('words')
    }
  })

  it('puts the pronunciation screens under Pronunciation', () => {
    expect(modeOf('pronunciation')).toBe('pronunciation')
    expect(modeOf('pronunciation-family')).toBe('pronunciation')
  })

  it('puts the verb screens under Irregular verbs', () => {
    expect(modeOf('verbs')).toBe('verbs')
    expect(modeOf('verbs-group')).toBe('verbs')
    expect(modeOf('verbs-session')).toBe('verbs')
  })

  it('leaves admin outside any mode', () => {
    expect(modeOf('admin')).toBeNull()
  })
})
