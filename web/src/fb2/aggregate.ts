import type { RawChapter } from './chapters'
import { lemmatizeText } from './lemmatize'
import { consolidateIngForms } from './tokenize'

export interface AggregatedWord {
  word: string
  count: number
}

export interface AggregatedChapter {
  order: number
  title: string
  words: AggregatedWord[]
}

/**
 * Chapters with raw text → chapters with base forms and frequencies.
 * The -ing consolidation runs over the whole book's vocabulary, not the chapter's:
 * the verb may be in one chapter and its gerund in an entirely different one.
 */
export function aggregate(chapters: RawChapter[]): AggregatedChapter[] {
  const perChapter = chapters.map((chapter) => {
    const counts = new Map<string, number>()

    for (const word of lemmatizeText(chapter.text)) {
      counts.set(word, (counts.get(word) ?? 0) + 1)
    }

    return { title: chapter.title, counts }
  })

  const vocabulary = new Set<string>()

  for (const chapter of perChapter) {
    for (const word of chapter.counts.keys()) {
      vocabulary.add(word)
    }
  }

  const kept = consolidateIngForms(vocabulary)

  const result: AggregatedChapter[] = []

  for (const chapter of perChapter) {
    const words: AggregatedWord[] = []

    for (const [word, count] of chapter.counts) {
      if (kept.has(word)) {
        words.push({ word, count })
      }
    }

    if (words.length === 0) {
      continue
    }

    words.sort((a, b) => b.count - a.count || a.word.localeCompare(b.word))
    result.push({ order: result.length, title: chapter.title, words })
  }

  return result
}
