import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  DefaultRounds,
  DefaultWords,
  MaxRounds,
  readIntroSettings,
  writeIntroSettings,
} from './introSettings'

const Key = 'll.verbs.intro'

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe('the remembered introduction settings', () => {
  it('opens on the defaults when nothing was stored', () => {
    expect(readIntroSettings(5)).toEqual({ words: DefaultWords, rounds: DefaultRounds })
  })

  it('remembers what was written', () => {
    writeIntroSettings({ words: 3, rounds: 2 })

    expect(readIntroSettings(5)).toEqual({ words: 3, rounds: 2 })
  })

  /// Review focus 1: a nearly finished stage offers fewer words than the learner last picked.
  it('never offers more words than the window holds', () => {
    writeIntroSettings({ words: 5, rounds: 3 })

    expect(readIntroSettings(2).words).toBe(2)
    expect(readIntroSettings(1).words).toBe(1)
  })

  it('clamps a stored value from outside the screen range', () => {
    window.localStorage.setItem(Key, JSON.stringify({ words: 99, rounds: 0 }))

    expect(readIntroSettings(5)).toEqual({ words: 5, rounds: 1 })

    window.localStorage.setItem(Key, JSON.stringify({ words: -4, rounds: 400 }))

    expect(readIntroSettings(5)).toEqual({ words: 1, rounds: MaxRounds })
  })

  it('falls back to the defaults on a value of the wrong shape', () => {
    window.localStorage.setItem(Key, JSON.stringify({ words: '3', rounds: null }))

    expect(readIntroSettings(5)).toEqual({ words: DefaultWords, rounds: DefaultRounds })
  })

  it('falls back to the defaults on unreadable storage', () => {
    window.localStorage.setItem(Key, 'not json at all')

    expect(readIntroSettings(5)).toEqual({ words: DefaultWords, rounds: DefaultRounds })
  })

  /// Review focus 1: a private window, or site data blocked outright.
  it('trains on the defaults when storage throws', () => {
    vi.spyOn(window.localStorage, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(readIntroSettings(5)).toEqual({ words: DefaultWords, rounds: DefaultRounds })
  })

  it('does not throw when storage refuses a write', () => {
    vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(() => writeIntroSettings({ words: 3, rounds: 3 })).not.toThrow()
  })
})
