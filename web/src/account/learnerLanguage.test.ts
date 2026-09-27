import { describe, expect, it } from 'vitest'
import { preselect } from './learnerLanguage'

describe('preselect', () => {
  it('prefers the current language, then the suggestion, then Ukrainian', () => {
    expect(preselect({ language: 'pl', suggestedLanguage: 'de' })).toBe('pl')
    expect(preselect({ language: null, suggestedLanguage: 'tl' })).toBe('tl')
    expect(preselect({ language: null, suggestedLanguage: null })).toBe('uk')
  })
})
