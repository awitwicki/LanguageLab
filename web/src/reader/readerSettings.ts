export type ReaderTheme = 'light' | 'dark' | 'system'

export interface ReaderSettings {
  theme: ReaderTheme
  /** 0..MAX_DIM: how far the page is darkened — the browser cannot change the screen's brightness. */
  dim: number
  /** An index into TEXT_SIZES. */
  textSize: number
  /**
   * Every sentence of the chapter in the DOM instead of a window around the reading place, so
   * find-in-page and a screen reader reach the whole of it. Costs the whole chapter's markup.
   */
  wholeChapter: boolean
}

export const TEXT_SIZES = [16, 18, 20, 23, 26]
export const MAX_DIM = 0.7
export const DEFAULT_SETTINGS: ReaderSettings = { theme: 'system', dim: 0, textSize: 2, wholeChapter: false }

const KEY = 'reader.settings'
const THEMES: ReaderTheme[] = ['light', 'dark', 'system']

/** A per-device convenience: storage that throws or holds garbage gives the defaults. */
export function loadSettings(): ReaderSettings {
  try {
    const raw = localStorage.getItem(KEY)

    return raw ? sanitize(JSON.parse(raw) as Partial<ReaderSettings>) : DEFAULT_SETTINGS
  } catch {
    return DEFAULT_SETTINGS
  }
}

export function saveSettings(settings: ReaderSettings) {
  try {
    localStorage.setItem(KEY, JSON.stringify(settings))
  } catch {
    // Not kept for next time — the reader still works.
  }
}

export function resolveTheme(theme: ReaderTheme, prefersDark: boolean): 'light' | 'dark' {
  return theme === 'system' ? (prefersDark ? 'dark' : 'light') : theme
}

function sanitize(value: Partial<ReaderSettings>): ReaderSettings {
  const theme = THEMES.includes(value.theme as ReaderTheme) ? (value.theme as ReaderTheme) : DEFAULT_SETTINGS.theme
  const dim = typeof value.dim === 'number' && Number.isFinite(value.dim) ? Math.min(Math.max(value.dim, 0), MAX_DIM) : 0
  const textSize = Number.isInteger(value.textSize)
    ? Math.min(Math.max(value.textSize as number, 0), TEXT_SIZES.length - 1)
    : DEFAULT_SETTINGS.textSize
  // Absent in settings stored before the whole chapter could be asked for: off, as it was then.
  const wholeChapter = typeof value.wholeChapter === 'boolean' ? value.wholeChapter : DEFAULT_SETTINGS.wholeChapter

  return { theme, dim, textSize, wholeChapter }
}
