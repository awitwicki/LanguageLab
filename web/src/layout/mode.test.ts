import { describe, expect, it } from 'vitest'
import { MODES, modeOf, visibleModes } from './mode'

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
    expect(modeOf('verbs-stage')).toBe('verbs')
    expect(modeOf('verbs-drill')).toBe('verbs')
  })

  it('leaves admin outside any mode', () => {
    expect(modeOf('admin')).toBeNull()
  })

  it('leaves the language picker outside any mode', () => {
    expect(modeOf('language')).toBeNull()
  })

  it('puts the library and the reader under Reading', () => {
    expect(modeOf('reader')).toBe('reading')
    expect(modeOf('reader-book')).toBe('reading')
  })

  it('offers Reading right after Words, and Grammar last', () => {
    expect(MODES.map((m) => m.mode)).toEqual(['words', 'reading', 'pronunciation', 'verbs', 'grammar'])
  })

  it('puts the grammar screens under Grammar', () => {
    expect(modeOf('grammar')).toBe('grammar')
    expect(modeOf('grammar-topic')).toBe('grammar')
  })
})

describe('visibleModes', () => {
  it('offers irregular verbs to Ukrainian learners only', () => {
    expect(visibleModes('uk').map((m) => m.mode)).toContain('verbs')
    expect(visibleModes('pl').map((m) => m.mode)).not.toContain('verbs')
  })

  it('offers grammar to every learner language', () => {
    for (const language of ['uk', 'pl', 'de', null]) {
      expect(visibleModes(language).map((m) => m.mode)).toContain('grammar')
    }
  })
})
