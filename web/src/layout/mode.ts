export type AppMode = 'words' | 'reading' | 'pronunciation' | 'verbs' | 'grammar'

export const MODES: { mode: AppMode; label: string }[] = [
  { mode: 'words', label: 'Words' },
  { mode: 'reading', label: 'Reading' },
  { mode: 'pronunciation', label: 'Pronunciation' },
  { mode: 'verbs', label: 'Irregular verbs' },
  { mode: 'grammar', label: 'Grammar' },
]

/** The top bar's modes for this learner: irregular verbs are Ukrainian-only (their translations are). */
export function visibleModes(language: string | null): typeof MODES {
  return MODES.filter((m) => m.mode !== 'verbs' || language === 'uk')
}

/** Which top-level mode a screen belongs to; null for screens outside all of them (admin, language). */
export function modeOf(routeName: string): AppMode | null {
  if (routeName.startsWith('reader')) return 'reading'
  if (routeName.startsWith('pronunciation')) return 'pronunciation'
  if (routeName.startsWith('verbs')) return 'verbs'
  if (routeName.startsWith('grammar')) return 'grammar'
  if (routeName === 'admin') return null
  if (routeName === 'language') return null
  return 'words'
}
