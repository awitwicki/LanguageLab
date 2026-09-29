import { afterEach, describe, expect, it, vi } from 'vitest'
import { DefaultWords, readWordCount, writeWordCount } from './sessionSettings'

const Key = 'll.verbs.intro'

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe('the remembered word count', () => {
  it('opens on the default when nothing was stored', () => {
    expect(readWordCount(5)).toBe(DefaultWords)
  })

  it('remembers what was written', () => {
    writeWordCount(3)

    expect(readWordCount(5)).toBe(3)
  })

  it('reads an old stored round-count shape, taking only the words', () => {
    window.localStorage.setItem(Key, JSON.stringify({ words: 3, rounds: 4 }))

    expect(readWordCount(5)).toBe(3)
  })

  /// Review focus 1: a nearly finished stage offers fewer words than the learner last picked.
  it('never offers more words than the window holds', () => {
    writeWordCount(5)

    expect(readWordCount(2)).toBe(2)
    expect(readWordCount(1)).toBe(1)
  })

  it('clamps a stored value from outside the screen range', () => {
    window.localStorage.setItem(Key, JSON.stringify({ words: 99 }))

    expect(readWordCount(5)).toBe(5)

    window.localStorage.setItem(Key, JSON.stringify({ words: -4 }))

    expect(readWordCount(5)).toBe(1)
  })

  it('falls back to the default on a value of the wrong shape', () => {
    window.localStorage.setItem(Key, JSON.stringify({ words: '3' }))

    expect(readWordCount(5)).toBe(DefaultWords)
  })

  it('falls back to the default on unreadable storage', () => {
    window.localStorage.setItem(Key, 'not json at all')

    expect(readWordCount(5)).toBe(DefaultWords)
  })

  /// Review focus 1: a private window, or site data blocked outright.
  it('trains on the default when storage throws', () => {
    vi.spyOn(window.localStorage, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(readWordCount(5)).toBe(DefaultWords)
  })

  it('does not throw when storage refuses a write', () => {
    vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(() => writeWordCount(3)).not.toThrow()
  })
})
