import { describe, expect, it } from 'vitest'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { parseReaderBook } from './readerBook'
import { bookLemmaCounts, EMPTY_STATUSES, resolveWord, toStatusMap } from './wordStatus'

const statuses = toStatusMap({ learning: ['adjust'], known: ['form', 'individual'] })

describe('resolveWord', () => {
  it('marks a word nobody has seen as new, under its base form', () => {
    expect(resolveWord('tried', EMPTY_STATUSES)).toEqual({ lemma: 'try', status: 'new' })
  })

  it('finds the standing of any inflected form', () => {
    expect(resolveWord('Adjust', statuses)).toEqual({ lemma: 'adjust', status: 'learning' })
    expect(resolveWord('formed', statuses)).toEqual({ lemma: 'form', status: 'known' })
    expect(resolveWord('individuals', statuses)).toEqual({ lemma: 'individual', status: 'known' })
  })

  it('never highlights function words and short words, but keeps them tappable', () => {
    expect(resolveWord('the', statuses)).toEqual({ lemma: 'the', status: null })
    expect(resolveWord("Don't", statuses)).toEqual({ lemma: "don't", status: null })
  })

  it('makes numbers and mixed tokens untappable', () => {
    expect(resolveWord('1984', statuses)).toBeNull()
    expect(resolveWord('b2b', statuses)).toBeNull()
  })
})

describe('bookLemmaCounts', () => {
  it('counts every form of a word across the whole book', () => {
    const counts = bookLemmaCounts(parseReaderBook(READER_BOOK_XML, 'x'))

    expect(counts.get('silo')).toBe(2)
    expect(counts.get('try')).toBe(1)
  })
})
