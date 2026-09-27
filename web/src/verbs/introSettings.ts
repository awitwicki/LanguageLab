/** The whole window, three rounds — what the start screen opens on. */
export const DefaultWords = 5
export const DefaultRounds = 3

/** More than five rounds of the same five words is drilling, not an introduction. */
export const MaxRounds = 5

const Key = 'll.verbs.intro'

export interface IntroSettings {
  words: number
  rounds: number
}

/**
 * What the learner last set, on this device. Storage can throw (a private window, site data
 * blocked) and can hold anything at all — an older build's shape, a hand-edited number — so
 * every read comes back clamped to what the screen can actually offer.
 */
export function readIntroSettings(maxWords: number): IntroSettings {
  try {
    const raw = window.localStorage.getItem(Key)
    const stored = raw ? (JSON.parse(raw) as Partial<IntroSettings>) : {}

    return {
      words: clamp(stored.words, DefaultWords, maxWords),
      rounds: clamp(stored.rounds, DefaultRounds, MaxRounds),
    }
  } catch {
    return { words: clamp(undefined, DefaultWords, maxWords), rounds: DefaultRounds }
  }
}

export function writeIntroSettings(settings: IntroSettings) {
  try {
    window.localStorage.setItem(Key, JSON.stringify(settings))
  } catch {
    // A device that cannot remember the setting still trains with it.
  }
}

function clamp(value: unknown, fallback: number, high: number): number {
  const number = typeof value === 'number' && Number.isFinite(value) ? Math.round(value) : fallback

  return Math.min(Math.max(number, 1), Math.max(high, 1))
}
