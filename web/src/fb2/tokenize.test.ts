import { describe, expect, it } from 'vitest'
import { cleanWord, consolidateIngForms, isRejected, splitCompoundWord } from './tokenize'

describe('cleanWord', () => {
  it('lowercases and strips punctuation but keeps hyphens', () => {
    expect(cleanWord('Well-Known,')).toBe('well-known')
  })

  it('strips quotes and underscores from the edges', () => {
    expect(cleanWord('"silo"')).toBe('silo')
  })
})

describe('splitCompoundWord', () => {
  it('splits on hyphens and keeps parts longer than two letters', () => {
    expect(splitCompoundWord('well-known').sort()).toEqual(['known', 'well'])
  })

  it('drops parts that are too short', () => {
    expect(splitCompoundWord('x-ray')).toEqual(['ray'])
  })
})

describe('isRejected', () => {
  it('rejects stopwords', () => {
    expect(isRejected('the')).toBe(true)
  })

  it('rejects words shorter than three letters', () => {
    expect(isRejected('go')).toBe(true)
  })

  it('rejects anything with digits', () => {
    expect(isRejected('21st')).toBe(true)
  })

  it('rejects ordinal number words', () => {
    expect(isRejected('fourth')).toBe(true)
    expect(isRejected('ninth')).toBe(true)
  })

  it('keeps ordinary words', () => {
    expect(isRejected('silo')).toBe(false)
  })
})

describe('consolidateIngForms', () => {
  it('drops an -ing form when its verb base is present', () => {
    const vocabulary = new Set(['surround', 'surrounding', 'silo'])

    expect([...consolidateIngForms(vocabulary)].sort()).toEqual(['silo', 'surround'])
  })

  it('keeps an -ing form when the base is absent', () => {
    const vocabulary = new Set(['surrounding'])

    expect([...consolidateIngForms(vocabulary)]).toEqual(['surrounding'])
  })

  it('keeps homographs that are nouns in their own right', () => {
    const vocabulary = new Set(['building', 'build'])

    expect([...consolidateIngForms(vocabulary)].sort()).toEqual(['build', 'building'])
  })
})
