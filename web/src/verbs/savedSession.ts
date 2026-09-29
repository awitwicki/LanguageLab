import type { DrillQuery, SessionVerb, VerbAnswerToPost } from '../api/client'
import type { DrillState, Played } from './session'

/** Long enough to cover a closed Mini App reopened later the same day, not a stale round. */
export const MaxAgeMs = 12 * 60 * 60 * 1000

/** v2: the choice drill. A round saved by the self-assessment drill has no exercises to show and is never read. */
const Key = 'll.verbs.session.v2'

/**
 * A round in progress, kept on this device so a reload can pick it up where it stopped. It
 * holds the state the learner lands on next — an answered card is already behind it — and
 * the answers that have not reached the server yet.
 */
export interface SavedSession {
  userId: number
  savedAt: number
  query: DrillQuery
  title: string
  /** `done` only while its last answers are still waiting to leave. */
  phase: 'drill' | 'done'
  /** The window on offer, and how much of it the learner took — the drill round's words. */
  offer: SessionVerb[]
  words: number
  scope: { passed: number; total: number }
  played: Played | null
  drill: DrillState | null
  pending: VerbAnswerToPost[]
}

/**
 * The saved round of this user, or null. Storage can throw and can hold anything at all — an
 * older build's shape, another account's round, yesterday's session — and whatever cannot be
 * resumed is removed on the way.
 */
export function readSavedSession(userId: number): SavedSession | null {
  try {
    const raw = window.localStorage.getItem(Key)

    if (!raw) {
      return null
    }

    const saved = JSON.parse(raw) as unknown

    if (isSavedSession(saved) && saved.userId === userId && Date.now() - saved.savedAt <= MaxAgeMs) {
      return saved
    }

    clearSavedSession()

    return null
  } catch {
    return null
  }
}

export function writeSavedSession(session: SavedSession) {
  try {
    window.localStorage.setItem(Key, JSON.stringify(session))
  } catch {
    // A device that cannot remember the round still trains; a reload starts over, as before.
  }
}

export function clearSavedSession() {
  try {
    window.localStorage.removeItem(Key)
  } catch {
    // Nothing was stored, then.
  }
}

/** Checks as much as the screen relies on; the cards themselves are taken as they were saved. */
function isSavedSession(value: unknown): value is SavedSession {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const v = value as Record<string, unknown>
  const query = v.query as Record<string, unknown> | null
  const scope = v.scope as Record<string, unknown> | null
  const played = v.played as Record<string, unknown> | null
  const drill = v.drill as Record<string, unknown> | null

  return (
    typeof v.userId === 'number' &&
    typeof v.savedAt === 'number' &&
    typeof query === 'object' &&
    query !== null &&
    typeof query.mode === 'string' &&
    typeof v.title === 'string' &&
    (v.phase === 'drill' || v.phase === 'done') &&
    Array.isArray(v.offer) &&
    typeof v.words === 'number' &&
    typeof scope === 'object' &&
    scope !== null &&
    typeof scope.passed === 'number' &&
    typeof scope.total === 'number' &&
    Array.isArray(v.pending) &&
    // Exactly one of the two rounds is being played.
    (played === null) !== (drill === null) &&
    (played === null || (played.kind === 'queue' && Array.isArray(played.cards) && typeof played.index === 'number')) &&
    (drill === null || (Array.isArray(drill.words) && typeof drill.dealt === 'number'))
  )
}
