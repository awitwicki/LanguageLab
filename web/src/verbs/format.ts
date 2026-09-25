import type { VerbLearnState } from '../api/client'

/** "go – went – gone" — the same triplet shape used in feedback and mistake lists. */
export function triplet(v1: string, v2: string, v3: string): string {
  return `${v1} – ${v2} – ${v3}`
}

const STATE_LABELS: Record<VerbLearnState, string> = {
  new: 'Not started',
  learning1: 'Learning',
  learning2: 'Learning',
  learning3: 'Learning',
  learned: 'Learned',
  mastered: 'Learned',
  forgotten: 'Forgotten',
}

export function stateLabel(state: VerbLearnState): string {
  return STATE_LABELS[state]
}

/** "-ought / -aught" from a family's suffix list; empty for families with no shared pattern (same, back, core). */
export function familyPattern(suffixes: string[]): string {
  return suffixes.map((s) => `-${s}`).join(' / ')
}
