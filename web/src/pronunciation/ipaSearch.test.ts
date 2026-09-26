import { describe, expect, it } from 'vitest'
import type { IpaEntry, IpaSection } from '../api/client'
import { countEntries, filterAlphabet, groupRuns, matches } from './ipaSearch'

function entry(over: Partial<IpaEntry> = {}): IpaEntry {
  return {
    symbol: 'ʃ',
    name: 'voiceless postalveolar fricative',
    hint: 'sh as in "ship"',
    group: 'Fricative',
    inEnglish: true,
    exampleWord: 'ship',
    exampleLanguage: 'English',
    exampleIpa: '/ʃɪp/',
    soundAudio: '/pronunciation-audio/ipa-voiceless-postalveolar-fricative.ogg',
    wordAudio: '/pronunciation-audio/ship-en.ogg',
    familyKey: null,
    aliases: [],
    ...over,
  }
}

function section(key: string, entries: IpaEntry[]): IpaSection {
  return { key, title: key, note: '', entries }
}

describe('matches', () => {
  it('finds a symbol pasted straight from a dictionary', () => {
    expect(matches(entry(), 'ʃ')).toBe(true)
  })

  it('finds a symbol by the letters it is called by', () => {
    expect(matches(entry(), 'sh')).toBe(true)
  })

  it('finds a symbol by the name of the sound', () => {
    expect(matches(entry(), 'postalveolar')).toBe(true)
    expect(matches(entry(), 'Fricative')).toBe(true)
  })

  it('finds a symbol by its example word', () => {
    expect(matches(entry(), 'ship')).toBe(true)
  })

  it('does not drag in every English row for a query the word "English" contains', () => {
    // "English" holds "sh", "li" and "ng"; searching the example's language made a search
    // for the sh-sound match every English-exampled symbol there is.
    expect(matches(entry({ symbol: 'p', name: 'voiceless bilabial plosive', hint: 'p as in "pen"', exampleWord: 'pen', exampleIpa: '/pɛn/' }), 'sh')).toBe(false)
    expect(matches(entry(), 'sh')).toBe(true)
  })

  it('still reaches a language through the hint that names it', () => {
    const x = entry({
      symbol: 'x',
      name: 'voiceless velar fricative',
      hint: 'as Ukrainian х — a rasped k',
      exampleWord: 'хата',
      exampleLanguage: 'Ukrainian',
      exampleIpa: '/ˈxata/',
      inEnglish: false,
    })

    expect(matches(x, 'ukrainian')).toBe(true)
  })

  it('finds a long vowel written with a length mark through its aliases', () => {
    const i = entry({ symbol: 'i', aliases: ['iː', 'i:'], name: 'close front unrounded vowel' })

    expect(matches(i, 'iː')).toBe(true)
    expect(matches(i, 'i:')).toBe(true)
  })

  it('ignores case and surrounding space', () => {
    expect(matches(entry(), '  SHIP ')).toBe(true)
  })

  it('keeps everything for an empty query', () => {
    expect(matches(entry(), '')).toBe(true)
    expect(matches(entry(), '   ')).toBe(true)
  })

  it('rejects what it does not hold', () => {
    expect(matches(entry(), 'trill')).toBe(false)
  })
})

describe('filterAlphabet', () => {
  const sections = [
    section('pulmonic', [
      entry(),
      entry({ symbol: 'r', name: 'alveolar trill', hint: 'a rolled r', inEnglish: false, exampleWord: 'рука', exampleLanguage: 'Ukrainian', exampleIpa: '/rʊˈka/' }),
    ]),
    section('vowels', [entry({ symbol: 'y', name: 'close front rounded vowel', hint: 'as French u', inEnglish: false, exampleWord: 'rue', exampleLanguage: 'French', exampleIpa: '/ʁy/' })]),
  ]

  it('keeps every section when nothing is asked of it', () => {
    expect(filterAlphabet(sections, '', false)).toEqual(sections)
  })

  it('drops a section a search empties, rather than leaving its heading standing', () => {
    const found = filterAlphabet(sections, 'trill', false)

    expect(found).toHaveLength(1)
    expect(found[0].key).toBe('pulmonic')
    expect(found[0].entries.map((e) => e.symbol)).toEqual(['r'])
  })

  it('narrows to the sounds English uses', () => {
    const found = filterAlphabet(sections, '', true)

    expect(found).toHaveLength(1)
    expect(found[0].entries.map((e) => e.symbol)).toEqual(['ʃ'])
  })

  it('applies the search and the English filter together', () => {
    expect(filterAlphabet(sections, 'trill', true)).toEqual([])
  })

  it('leaves the source untouched', () => {
    filterAlphabet(sections, 'trill', true)

    expect(sections[0].entries).toHaveLength(2)
  })
})

describe('countEntries', () => {
  it('counts the symbols across every section', () => {
    expect(countEntries([section('a', [entry(), entry()]), section('b', [entry()])])).toBe(3)
  })

  it('counts nothing as nothing', () => {
    expect(countEntries([])).toBe(0)
  })
})

describe('groupRuns', () => {
  it('splits a section into the chart rows it holds, in order', () => {
    const runs = groupRuns([
      entry({ symbol: 'p', group: 'Plosive' }),
      entry({ symbol: 'b', group: 'Plosive' }),
      entry({ symbol: 'm', group: 'Nasal' }),
    ])

    expect(runs.map((run) => run.group)).toEqual(['Plosive', 'Nasal'])
    expect(runs[0].entries.map((e) => e.symbol)).toEqual(['p', 'b'])
    expect(runs[1].entries.map((e) => e.symbol)).toEqual(['m'])
  })

  it('starts a new run when a group comes back later', () => {
    const runs = groupRuns([
      entry({ symbol: 'p', group: 'Plosive' }),
      entry({ symbol: 'm', group: 'Nasal' }),
      entry({ symbol: 't', group: 'Plosive' }),
    ])

    expect(runs.map((run) => run.group)).toEqual(['Plosive', 'Nasal', 'Plosive'])
  })

  it('has no runs for no entries', () => {
    expect(groupRuns([])).toEqual([])
  })
})
