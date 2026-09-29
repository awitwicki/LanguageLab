import { describe, expect, it } from 'vitest'
import { DefaultWords, resolveWordCount } from './sessionSettings'

describe('the resolved word count', () => {
  it('opens on the default when the account has never set one', () => {
    expect(resolveWordCount(null, 5)).toBe(DefaultWords)
    expect(resolveWordCount(undefined, 5)).toBe(DefaultWords)
  })

  it('uses the account value', () => {
    expect(resolveWordCount(3, 5)).toBe(3)
  })

  /// Review focus 1: a nearly finished stage offers fewer words than the account last chose.
  it('never offers more words than the window holds', () => {
    expect(resolveWordCount(5, 2)).toBe(2)
    expect(resolveWordCount(5, 1)).toBe(1)
  })

  it('clamps an account value from outside the screen range', () => {
    expect(resolveWordCount(99, 5)).toBe(5)
    expect(resolveWordCount(-4, 5)).toBe(1)
  })

  it('falls back to the default on a value of the wrong shape', () => {
    expect(resolveWordCount(Number.NaN, 5)).toBe(DefaultWords)
  })
})
