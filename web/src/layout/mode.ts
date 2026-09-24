export type AppMode = 'words' | 'reading' | 'pronunciation' | 'verbs'

export const MODES: { mode: AppMode; label: string }[] = [
  { mode: 'words', label: 'Words' },
  { mode: 'reading', label: 'Reading' },
  { mode: 'pronunciation', label: 'Pronunciation' },
  { mode: 'verbs', label: 'Irregular verbs' },
]

/** Which top-level mode a screen belongs to; null for screens outside all of them (admin). */
export function modeOf(routeName: string): AppMode | null {
  if (routeName.startsWith('reader')) return 'reading'
  if (routeName.startsWith('pronunciation')) return 'pronunciation'
  if (routeName.startsWith('verbs')) return 'verbs'
  if (routeName === 'admin') return null
  return 'words'
}
