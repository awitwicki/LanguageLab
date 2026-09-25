import { describe, expect, it } from 'vitest'
import { allChunks, chunkChapter, chunkOfPosition, chunksAround } from './chapterWindow'
import type { ReaderChapter, ReaderSentence } from './readerBook'

const sentence = (text: string): ReaderSentence => ({ text, tokens: [{ text, isWord: true }] })

/** A chapter of `paragraphs` paragraphs, each holding `per` sentences. */
const chapterOf = (paragraphs: number, per: number): ReaderChapter => ({
  title: 'Long',
  paragraphs: Array.from({ length: paragraphs }, (_, p) => ({
    sentences: Array.from({ length: per }, (_, s) => sentence(`p${p}s${s}.`)),
  })),
})

describe('chunkChapter', () => {
  it("cuts the chapter's sentences into chunks of the given size, in reading order", () => {
    const chunks = chunkChapter(chapterOf(5, 3), 2, 4)

    expect(chunks.map((chunk) => chunk.length)).toEqual([4, 4, 4, 3])
    expect(chunks[0].map((entry) => entry.key)).toEqual(['2.0.0', '2.0.1', '2.0.2', '2.1.0'])
    expect(chunks.at(-1)!.at(-1)!.sentence.text).toBe('p4s2.')
  })

  it("marks the first sentence of every paragraph but the chapter's own first", () => {
    const chunks = chunkChapter(chapterOf(3, 2), 0, 100)

    expect(chunks[0].map((entry) => entry.paragraphStart)).toEqual([false, false, true, false, true, false])
  })

  it('has no chunks for a chapter with no sentences', () => {
    expect(chunkChapter({ title: '', paragraphs: [] }, 0)).toEqual([])
  })
})

describe('chunkOfPosition', () => {
  it('finds the chunk holding a position', () => {
    const chapter = chapterOf(10, 3)

    expect(chunkOfPosition(chapter, { chapterIndex: 0, paragraphIndex: 0, sentenceIndex: 0 }, 4)).toBe(0)
    expect(chunkOfPosition(chapter, { chapterIndex: 0, paragraphIndex: 1, sentenceIndex: 1 }, 4)).toBe(1)
    expect(chunkOfPosition(chapter, { chapterIndex: 0, paragraphIndex: 9, sentenceIndex: 2 }, 4)).toBe(7)
  })

  it('gives the last chunk for a position past the end of the chapter', () => {
    expect(chunkOfPosition(chapterOf(2, 2), { chapterIndex: 0, paragraphIndex: 40, sentenceIndex: 0 }, 4)).toBe(0)
    expect(chunkOfPosition(chapterOf(5, 2), { chapterIndex: 0, paragraphIndex: 40, sentenceIndex: 0 }, 4)).toBe(2)
  })
})

describe('chunksAround', () => {
  it('takes the radius on both sides, clamped to the chapter', () => {
    expect(chunksAround(4, 10, 2)).toEqual([2, 3, 4, 5, 6])
    expect(chunksAround(0, 10, 2)).toEqual([0, 1, 2])
    expect(chunksAround(9, 10, 2)).toEqual([7, 8, 9])
    expect(chunksAround(0, 0, 2)).toEqual([])
  })
})

describe('allChunks', () => {
  it('is every chunk index', () => {
    expect(allChunks(3)).toEqual([0, 1, 2])
    expect(allChunks(0)).toEqual([])
  })
})
