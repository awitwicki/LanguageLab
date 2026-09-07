import { describe, expect, it } from 'vitest'
import { aggregate } from './aggregate'

describe('aggregate', () => {
  it('counts occurrences per chapter', () => {
    const [chapter] = aggregate([
      { title: 'One', text: 'The silo was quiet. The silo was cold. Holston climbed.' },
    ])

    const counts = Object.fromEntries(chapter.words.map((w) => [w.word, w.count]))

    expect(counts.silo).toBe(2)
    expect(counts.holston).toBe(1)
  })

  it('drops stopwords and short words', () => {
    const [chapter] = aggregate([{ title: 'One', text: 'The silo is on a hill.' }])

    const words = chapter.words.map((w) => w.word)

    expect(words).not.toContain('the')
    expect(words).not.toContain('is')
    expect(words).not.toContain('on')
  })

  it('reduces inflected forms to their base', () => {
    const [chapter] = aggregate([{ title: 'One', text: 'He climbed and she climbs.' }])

    const counts = Object.fromEntries(chapter.words.map((w) => [w.word, w.count]))

    expect(counts.climb).toBe(2)
  })

  it('numbers chapters in order', () => {
    const chapters = aggregate([
      { title: 'One', text: 'silo silo' },
      { title: 'Two', text: 'holston' },
    ])

    expect(chapters.map((c) => c.order)).toEqual([0, 1])
    expect(chapters.map((c) => c.title)).toEqual(['One', 'Two'])
  })

  it('drops chapters that yield no words', () => {
    const chapters = aggregate([
      { title: 'Empty', text: 'the and of' },
      { title: 'Real', text: 'silo' },
    ])

    expect(chapters.map((c) => c.title)).toEqual(['Real'])
  })

  it('never synthesizes a hyphen-stripped concatenation for compounds compromise leaves as one token', () => {
    const [chapter] = aggregate([
      {
        title: 'One',
        text: 'The doctor used an x-ray machine to look at the ray of light near the silo.',
      },
    ])

    const words = chapter.words.map((w) => w.word)

    expect(words).not.toContain('xray')
  })
})
