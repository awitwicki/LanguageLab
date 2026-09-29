/** The whole window — what the start screen opens on when the account has never set one. */
export const DefaultWords = 5

/**
 * How many words to open the start screen on, given the account's last choice (`null` for an
 * account that has never started a round) and how many the current offer actually holds — a
 * nearly finished stage can offer fewer than what was last picked.
 */
export function resolveWordCount(accountWords: number | null | undefined, maxWords: number): number {
  const number =
    typeof accountWords === 'number' && Number.isFinite(accountWords) ? Math.round(accountWords) : DefaultWords

  return Math.min(Math.max(number, 1), Math.max(maxWords, 1))
}
