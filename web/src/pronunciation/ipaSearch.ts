import type { IpaEntry, IpaSection } from '../api/client'

/** A section with only the entries that survived a filter, or null when none did. */
function narrow(section: IpaSection, keep: (entry: IpaEntry) => boolean): IpaSection | null {
  const entries = section.entries.filter(keep)
  return entries.length === 0 ? null : { ...section, entries }
}

/**
 * Whether one symbol answers a query. A learner arrives here from a dictionary entry with
 * a symbol to paste ("ʒ"), or with only the letters they heard it called by ("zh", "sh"),
 * or knowing the name of the sound ("trill") — all three have to find the row, so the
 * query is matched against the symbol, its aliases, its name, its hint and its example.
 *
 * The example's language is deliberately not searched: "English" contains "sh", "li" and
 * "ng", so including it made a search for the sh-sound return every English row there is
 * and bury ʃ among them. A language is still reachable where it says something about the
 * sound, because the hint names it ("as Ukrainian х").
 */
export function matches(entry: IpaEntry, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (needle === '') return true

  const haystack = [
    entry.symbol,
    ...entry.aliases,
    entry.name,
    entry.hint,
    entry.group,
    entry.exampleWord ?? '',
    entry.exampleIpa ?? '',
  ]

  return haystack.some((value) => value.toLowerCase().includes(needle))
}

/**
 * The chart as the screen shows it: sections in catalog order, emptied sections dropped
 * so a search never leaves a heading standing over nothing.
 */
export function filterAlphabet(
  sections: IpaSection[],
  query: string,
  englishOnly: boolean,
): IpaSection[] {
  return sections
    .map((section) => narrow(section, (entry) => (!englishOnly || entry.inEnglish) && matches(entry, query)))
    .filter((section): section is IpaSection => section !== null)
}

/** How many symbols a filtered chart holds — what the result count reports. */
export function countEntries(sections: IpaSection[]): number {
  return sections.reduce((total, section) => total + section.entries.length, 0)
}

/**
 * The entries of one section split into the runs the chart's rows make ("Plosive",
 * "Nasal", …), in the order they appear. A group heading is only worth drawing when the
 * section has more than one, which the screen decides from the length of this list.
 */
export function groupRuns(entries: IpaEntry[]): { group: string; entries: IpaEntry[] }[] {
  const runs: { group: string; entries: IpaEntry[] }[] = []
  for (const entry of entries) {
    const last = runs[runs.length - 1]
    if (last && last.group === entry.group) {
      last.entries.push(entry)
    } else {
      runs.push({ group: entry.group, entries: [entry] })
    }
  }

  return runs
}
