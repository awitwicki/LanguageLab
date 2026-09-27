import { afterEach, describe, expect, it, vi } from 'vitest'
import type { SessionVerb } from '../api/client'
import { clearSavedSession, MaxAgeMs, readSavedSession, writeSavedSession, type SavedSession } from './savedSession'
import { createDrill } from './session'

const Key = 'll.verbs.session'

function verb(v1: string): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 4,
    note: null,
    examples: { present: `I [${v1}].`, past: `I [${v1}-2].`, perfect: `I have [${v1}-3].` },
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
    fresh: true,
  }
}

function saved(overrides: Partial<SavedSession> = {}): SavedSession {
  const verbs = [verb('a'), verb('b')]

  return {
    userId: 7,
    savedAt: Date.now(),
    query: { mode: 'batch', group: 4 },
    title: 'All three forms differ',
    phase: 'drill',
    offer: verbs,
    words: 2,
    scope: { passed: 2, total: 27 },
    played: null,
    drill: createDrill(verbs),
    pending: [],
    ...overrides,
  }
}

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe('the saved verbs session', () => {
  it('is empty when nothing was saved', () => {
    expect(readSavedSession(7)).toBeNull()
  })

  it('reads back what was written', () => {
    const session = saved()
    writeSavedSession(session)

    expect(readSavedSession(7)).toEqual(session)
  })

  it('is gone once cleared', () => {
    writeSavedSession(saved())
    clearSavedSession()

    expect(readSavedSession(7)).toBeNull()
  })

  /// Another account signed in on this device must not be handed someone else's round.
  it('belongs to the user who saved it', () => {
    writeSavedSession(saved({ userId: 7 }))

    expect(readSavedSession(8)).toBeNull()
    expect(window.localStorage.getItem(Key)).toBeNull()
  })

  it('expires', () => {
    writeSavedSession(saved({ savedAt: Date.now() - MaxAgeMs - 1 }))

    expect(readSavedSession(7)).toBeNull()
    expect(window.localStorage.getItem(Key)).toBeNull()
  })

  it('drops a value of the wrong shape', () => {
    window.localStorage.setItem(Key, JSON.stringify({ ...saved(), phase: 'start' }))
    expect(readSavedSession(7)).toBeNull()

    window.localStorage.setItem(Key, JSON.stringify({ ...saved(), drill: null }))
    expect(readSavedSession(7)).toBeNull()

    window.localStorage.setItem(Key, JSON.stringify({ ...saved(), pending: 'nope' }))
    expect(readSavedSession(7)).toBeNull()
  })

  it('drops unreadable storage', () => {
    window.localStorage.setItem(Key, 'not json at all')

    expect(readSavedSession(7)).toBeNull()
  })

  /// A private window, or site data blocked outright.
  it('reads as empty when storage throws', () => {
    vi.spyOn(window.localStorage, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(readSavedSession(7)).toBeNull()
  })

  it('does not throw when storage refuses a write', () => {
    vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(() => writeSavedSession(saved())).not.toThrow()
  })
})
