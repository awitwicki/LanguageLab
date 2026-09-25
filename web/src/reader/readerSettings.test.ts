import { afterEach, describe, expect, it, vi } from 'vitest'
import { DEFAULT_SETTINGS, loadSettings, resolveTheme, saveSettings } from './readerSettings'

afterEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('reader settings', () => {
  it('round-trip through localStorage', () => {
    saveSettings({ theme: 'dark', dim: 0.3, textSize: 4 })

    expect(loadSettings()).toEqual({ theme: 'dark', dim: 0.3, textSize: 4 })
  })

  it('fall back to the defaults when storage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(loadSettings()).toEqual(DEFAULT_SETTINGS)
  })

  it('repair values out of range or of the wrong kind', () => {
    localStorage.setItem('reader.settings', JSON.stringify({ theme: 'sepia', dim: 5, textSize: -3 }))

    expect(loadSettings()).toEqual({ theme: 'system', dim: 0.7, textSize: 0 })
  })

  it('resolve System to the device preference', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
    expect(resolveTheme('light', true)).toBe('light')
  })
})
