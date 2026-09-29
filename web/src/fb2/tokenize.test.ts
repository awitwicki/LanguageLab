import { describe, expect, it } from 'vitest'
import { cleanWord, isRejected } from './tokenize'

describe('cleanWord', () => {
  it('lowercases and strips punctuation but keeps hyphens', () => {
    expect(cleanWord('Well-Known,')).toBe('well-known')
  })

  it('strips quotes and underscores from the edges', () => {
    expect(cleanWord('"silo"')).toBe('silo')
  })

  it('keeps a straight apostrophe inside a contraction', () => {
    expect(cleanWord("Don't")).toBe("don't")
  })

  it('normalizes a typographic apostrophe to a straight one', () => {
    expect(cleanWord('wasn’t')).toBe("wasn't")
  })
})

describe('isRejected', () => {
  it('rejects stopwords', () => {
    expect(isRejected('the')).toBe(true)
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

  it('no longer rejects by length alone — that now depends on the lexicon (wordStatus.ts)', () => {
    expect(isRejected('go')).toBe(false)
  })

  it('rejects contractions instead of leaking a stripped-apostrophe form', () => {
    expect(isRejected(cleanWord('wasn’t'))).toBe(true)
    expect(isRejected(cleanWord('you’re'))).toBe(true)
    expect(isRejected(cleanWord("that's"))).toBe(true)
  })
})
