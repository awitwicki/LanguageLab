/** How well a verb is known, as a colour band; `none` is a verb never answered. */
export type MasteryBand = 'none' | 'low' | 'mid' | 'high' | 'top'

/// The bar's length carries the exact level, so the colour only has to say which zone
/// the verb is in — red, orange, yellow, green.
export function masteryBand(mastery: number, answers: number): MasteryBand {
  if (answers === 0) return 'none'
  if (mastery < 0.35) return 'low'
  if (mastery < 0.6) return 'mid'
  if (mastery < 0.8) return 'high'
  return 'top'
}

export function masteryPercent(mastery: number): number {
  return Math.round(mastery * 100)
}
