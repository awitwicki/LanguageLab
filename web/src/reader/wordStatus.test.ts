import { describe, expect, it } from 'vitest'
import type { Lexicon } from '../lexicon/lexicon'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { parseReaderBook } from './readerBook'
import { bookLemmaCounts, EMPTY_STATUSES, resolveWord, toStatusMap } from './wordStatus'

function fakeLexicon(entries: Record<string, string[]>): Lexicon {
  return {
    lemmasOf: (form) => entries[form] ?? [],
    lemmaOf: (form) => entries[form]?.[0] ?? null,
  }
}

const lexicon = fakeLexicon({
  tried: ['try'],
  try: ['try'],
  adjust: ['adjust'],
  formed: ['form'],
  form: ['form'],
  individuals: ['individual'],
  individual: ['individual'],
  silo: ['silo'],
  go: ['go'],
  // A real lexicon entry (english-lexicon.txt): the form is its own primary lemma, but also
  // lists a rarer alternate after it — "an inflection wins" only when the form isn't the primary.
  morning: ['morning', 'morn'],
})

const statuses = toStatusMap({ learning: ['adjust'], known: ['form', 'individual'] })

describe('resolveWord', () => {
  it('marks a word nobody has seen as new, under its base form', () => {
    expect(resolveWord('tried', EMPTY_STATUSES, lexicon)).toEqual({ lemma: 'try', status: 'new' })
  })

  // Final review, Important 3: lemmasOf is primary-first, so the form itself must win when it is
  // its own primary lemma — even when a rarer alternate lemma follows it in the list.
  it('marks a word as new under its own form when the lexicon says the form is the primary lemma', () => {
    expect(resolveWord('morning', EMPTY_STATUSES, lexicon)).toEqual({ lemma: 'morning', status: 'new' })
  })

  it('finds the standing of any inflected form', () => {
    expect(resolveWord('Adjust', statuses, lexicon)).toEqual({ lemma: 'adjust', status: 'learning' })
    expect(resolveWord('formed', statuses, lexicon)).toEqual({ lemma: 'form', status: 'known' })
    expect(resolveWord('individuals', statuses, lexicon)).toEqual({ lemma: 'individual', status: 'known' })
  })

  it('never highlights function words and short words, but keeps them tappable', () => {
    expect(resolveWord('the', statuses, lexicon)).toEqual({ lemma: 'the', status: null })
    expect(resolveWord("Don't", statuses, lexicon)).toEqual({ lemma: "don't", status: null })
  })

  it('makes numbers and mixed tokens untappable', () => {
    expect(resolveWord('1984', statuses, lexicon)).toBeNull()
    expect(resolveWord('b2b', statuses, lexicon)).toBeNull()
  })

  it('treats a word the lexicon does not know as tappable but never highlighted', () => {
    expect(resolveWord('frodo', statuses, lexicon)).toEqual({ lemma: 'frodo', status: null })
  })

  it('accepts a two-letter form only when the lexicon knows it as a lemma', () => {
    expect(resolveWord('go', EMPTY_STATUSES, lexicon)).toEqual({ lemma: 'go', status: 'new' })
    expect(resolveWord('xy', EMPTY_STATUSES, lexicon)).toEqual({ lemma: 'xy', status: null })
  })

  it('never highlights while the lexicon has not loaded yet', () => {
    expect(resolveWord('tried', statuses, null)).toEqual({ lemma: 'tried', status: null })
    expect(resolveWord('the', statuses, null)).toEqual({ lemma: 'the', status: null })
  })

  // Review Focus: a word asked about while the lexicon was still null must not get stuck on
  // that answer — it must resolve properly once the lexicon has loaded.
  it('gives a real answer once the lexicon loads, even if asked before while it was still null', () => {
    expect(resolveWord('tried', EMPTY_STATUSES, null)).toEqual({ lemma: 'tried', status: null })
    expect(resolveWord('tried', EMPTY_STATUSES, lexicon)).toEqual({ lemma: 'try', status: 'new' })
  })
})

describe('bookLemmaCounts', () => {
  it('counts every form of a word across the whole book', () => {
    const counts = bookLemmaCounts(parseReaderBook(READER_BOOK_XML, 'x'), lexicon)

    expect(counts.get('silo')).toBe(2)
    expect(counts.get('try')).toBe(1)
  })
})
