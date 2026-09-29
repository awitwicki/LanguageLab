/** The whole window — what the start screen opens on. */
export const DefaultWords = 5

/** Kept under the introduction's old key: the word count means the same thing it did. */
const Key = 'll.verbs.intro'

/**
 * How many words the learner last took, on this device. Storage can throw (a private window,
 * site data blocked) and can hold anything at all — an older build's shape, a hand-edited
 * number — so every read comes back clamped to what the window can actually offer.
 */
export function readWordCount(maxWords: number): number {
  try {
    const raw = window.localStorage.getItem(Key)
    const stored = raw ? (JSON.parse(raw) as { words?: unknown }) : {}

    return clamp(stored.words, maxWords)
  } catch {
    return clamp(undefined, maxWords)
  }
}

export function writeWordCount(words: number) {
  try {
    window.localStorage.setItem(Key, JSON.stringify({ words }))
  } catch {
    // A device that cannot remember the setting still trains with it.
  }
}

function clamp(value: unknown, high: number): number {
  const number = typeof value === 'number' && Number.isFinite(value) ? Math.round(value) : DefaultWords

  return Math.min(Math.max(number, 1), Math.max(high, 1))
}
