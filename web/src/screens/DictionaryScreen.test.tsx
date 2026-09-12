import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DictionaryDetail, LearningProgress, TrainingStarted } from '../api/client'
import { click, flush, render } from '../test/render'
import { DictionaryScreen } from './DictionaryScreen'

const apiMock = vi.hoisted(() => ({ getDictionary: vi.fn(), startReview: vi.fn(), setDictionaryVisibility: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const noLearning: LearningProgress = { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }
// notStarted == learnableCount (42) — так само, як на сервері. (9 + 10 + 12 + 60) / (5 · 71) → 26%.
const holstonLearning: LearningProgress = { notStarted: 42, boxes: [9, 5, 0, 3, 0], learned: 12, total: 71 }
const dueLearning: LearningProgress = { notStarted: 0, boxes: [5, 4, 0, 0, 0], learned: 1, total: 10 }
const waitingLearning: LearningProgress = { notStarted: 0, boxes: [2, 6, 0, 0, 0], learned: 0, total: 8 }
// formatDue compares UTC days, so "now + 24h" always lands on tomorrow.
const tomorrow = new Date(Date.now() + 86_400_000).toISOString()

const detail: DictionaryDetail = {
  id: 7,
  name: 'Wool',
  wordsCount: 2000,
  sortedCount: 500,
  learnableCount: 42,
  dueCount: 3,
  learning: holstonLearning,
  chapters: [
    { id: 11, order: 0, title: 'Holston', wordsCount: 300, sortedCount: 150, learnableCount: 42, learning: holstonLearning, dueCount: 0, nextDueAt: null },
    { id: 12, order: 1, title: '', wordsCount: 100, sortedCount: 100, learnableCount: 0, learning: noLearning, dueCount: 0, nextDueAt: null },
    // Every word started, five of them due: the row offers a review instead of an exercise.
    { id: 13, order: 2, title: 'Juliette', wordsCount: 80, sortedCount: 80, learnableCount: 0, learning: dueLearning, dueCount: 5, nextDueAt: null },
    // Every word started, none due until tomorrow: the row says so and waits.
    { id: 14, order: 3, title: 'Lukas', wordsCount: 60, sortedCount: 60, learnableCount: 0, learning: waitingLearning, dueCount: 0, nextDueAt: tomorrow },
  ],
  topWords: [
    { wordPairId: 1, word: 'silo', frequency: 1500 },
    { wordPairId: 2, word: 'abide', frequency: 750 },
  ],
  isPublic: true,
}

const reviewStarted: TrainingStarted = { trainingId: 9, mode: 'review', words: [], totalQuestions: 12 }

function screen(overrides: Partial<Parameters<typeof DictionaryScreen>[0]> = {}) {
  return <DictionaryScreen id={7} role="user" onSort={() => {}} onTrain={() => {}} onReview={() => {}} {...overrides} />
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.getDictionary.mockResolvedValue(detail)
  apiMock.startReview.mockResolvedValue(reviewStarted)
  apiMock.setDictionaryVisibility.mockResolvedValue(null)
})

describe('DictionaryScreen — chapters', () => {
  it('clicking a chapter title sorts only it; "Exercise" trains only it', async () => {
    const onSort = vi.fn()
    const onTrain = vi.fn()
    const { container } = await render(screen({ onSort, onTrain }))
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    expect(rows).toHaveLength(4)
    expect(rows[0].querySelector('.chapter-title')?.textContent).toBe('Holston')
    expect(rows[0].querySelector('.chapter-sub')?.textContent).toBe('300 words · 42 to learn')
    expect(rows[0].querySelector('.chapter-pct')?.textContent).toBe('50%')
    expect(container.querySelector('input[type="checkbox"]')).toBeNull()

    await click(rows[0].querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith([11], 'Holston')

    await click(rows[0].querySelector('.chapter-train')!)
    expect(onTrain).toHaveBeenCalledWith([11], 'Holston')
  })

  it('an untitled chapter is numbered; "Exercise" is disabled with nothing to learn', async () => {
    const onSort = vi.fn()
    const { container } = await render(screen({ onSort }))
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    expect(rows[1].querySelector('.chapter-title')?.textContent).toBe('Chapter 2')
    expect(rows[1].classList.contains('done')).toBe(true)
    expect((rows[1].querySelector('.chapter-train') as HTMLButtonElement).disabled).toBe(true)

    await click(rows[1].querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith([12], 'Chapter 2')
  })

  it('a flat dictionary has no chapters section', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, chapters: [] })
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.chapter-list')).toBeNull()
    expect(container.textContent).toContain('has no chapters')
  })

  it('a chapter\'s third line is the Leitner scale; absent without "don\'t know" words', async () => {
    const { container } = await render(screen())
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    const scale = rows[0].querySelector('.chapter-learning .leitner')!
    expect(scale).not.toBeNull()
    expect(scale.querySelector('.leitner-percent')?.textContent).toBe('26%')
    // Шкала — поза кнопкою сортування: її aria-label не має засмічувати назву кнопки.
    expect(rows[0].querySelector('.chapter-main .leitner')).toBeNull()
    expect(rows[1].querySelector('.chapter-learning')).toBeNull()
  })

  it('two columns: chapters on the left (first in the DOM), most frequent words on the right', async () => {
    const { container } = await render(screen())
    await flush()

    const columns = container.querySelector('.dict-columns')!
    expect(columns.children).toHaveLength(2)
    expect(columns.children[0].querySelector('.chapter-list')).not.toBeNull()
    expect(columns.children[1].querySelector('.top-words')).not.toBeNull()
  })
})

describe('DictionaryScreen — chapter review', () => {
  const row = (container: HTMLElement, index: number) => container.querySelectorAll('.chapter-row')[index]

  it('a chapter with due words says so and its button becomes "Review"', async () => {
    const { container } = await render(screen())
    await flush()

    const juliette = row(container, 2)
    expect(juliette.querySelector('.chapter-sub')?.textContent).toBe('80 words · 5 to review')

    const button = juliette.querySelector<HTMLButtonElement>('.chapter-train')!
    expect(button.textContent).toBe('Review')
    expect(button.disabled).toBe(false)
  })

  it('"Review" on a chapter starts a review scoped to it and hands back the chapter as the scope', async () => {
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    await click(row(container, 2).querySelector('.chapter-train')!)
    await flush()

    expect(apiMock.startReview).toHaveBeenCalledWith({ dictionaryId: 7, chapterIds: [13] })
    expect(onReview).toHaveBeenCalledWith(reviewStarted, 'Juliette')
  })

  // The hint is in the sub-line, not a tooltip: on a phone a title attribute never shows.
  it('a chapter with nothing due yet says how many are in progress and when the next one is due', async () => {
    const { container } = await render(screen())
    await flush()

    const lukas = row(container, 3)
    expect(lukas.querySelector('.chapter-sub')?.textContent).toBe('60 words · 8 in progress · next review tomorrow')

    const button = lukas.querySelector<HTMLButtonElement>('.chapter-train')!
    expect(button.textContent).toBe('Exercise')
    expect(button.disabled).toBe(true)
  })

  it('a chapter with new words keeps "Exercise" even when some of its words are due', async () => {
    apiMock.getDictionary.mockResolvedValue({
      ...detail,
      chapters: [{ ...detail.chapters[0], dueCount: 3 }],
    })
    const { container } = await render(screen())
    await flush()

    expect(row(container, 0).querySelector('.chapter-sub')?.textContent).toBe('300 words · 42 to learn')
    expect(row(container, 0).querySelector('.chapter-train')?.textContent).toBe('Exercise')
  })
})

describe('DictionaryScreen — actions', () => {
  it('"Sort the whole book" → onSort(null, "Whole book")', async () => {
    const onSort = vi.fn()
    const { container } = await render(screen({ onSort }))
    await flush()

    await click(buttons(container).find((b) => b.textContent?.includes('Sort the whole book'))!)

    expect(onSort).toHaveBeenCalledWith(null, 'Whole book')
  })

  it('"Start exercise" → onTrain(null, "Whole book")', async () => {
    const onTrain = vi.fn()
    const { container } = await render(screen({ onTrain }))
    await flush()

    const start = buttons(container).find((b) => b.textContent?.includes('Start exercise'))!
    expect(start.disabled).toBe(false)

    await click(start)

    expect(onTrain).toHaveBeenCalledWith(null, 'Whole book')
  })

  it('with nothing to learn, "Start exercise" is disabled and a hint is shown', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, learnableCount: 0 })
    const { container } = await render(screen())
    await flush()

    expect(buttons(container).find((b) => b.textContent?.includes('Start exercise'))!.disabled).toBe(true)
    expect(container.textContent).toContain('No words to learn yet')
  })

  it('"Review (3)" appears when dueCount > 0 and starts a review', async () => {
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Review (3)')!)
    await flush()

    expect(apiMock.startReview).toHaveBeenCalledTimes(1)
    expect(onReview).toHaveBeenCalledWith(reviewStarted, undefined)
  })

  // The header's button is the global review; a chapter row may still offer its own.
  it('no header review button without due words', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, dueCount: 0 })
    const { container } = await render(screen())
    await flush()

    const header = [...container.querySelectorAll<HTMLButtonElement>('.dict-actions button')]
    expect(header.find((b) => b.textContent?.startsWith('Review'))).toBeUndefined()
  })

  it('the header carries the book scale captioned "Learned"; absent without "don\'t know" words', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.dict-header .dict-learning .leitner-percent')?.textContent).toBe('26%')
    expect(container.querySelector('.dict-learning')?.textContent).toContain('Learned')

    apiMock.getDictionary.mockResolvedValue({ ...detail, learning: noLearning })
    const empty = await render(screen())
    await flush()

    expect(empty.container.querySelector('.dict-learning')).toBeNull()
  })

  it('a review with no words (204) → a notice, onReview not called', async () => {
    apiMock.startReview.mockResolvedValue(null)
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Review (3)')!)
    await flush()

    expect(onReview).not.toHaveBeenCalled()
    expect(container.textContent).toContain('Nothing to review today')
  })
})

describe('DictionaryScreen — stats', () => {
  it('lists the top words by descending frequency, numbered', async () => {
    const { container } = await render(screen())
    await flush()

    const rows = [...container.querySelectorAll('.top-word')]
    expect(rows.map((r) => r.querySelector('.rank')?.textContent)).toEqual(['1', '2'])
    expect(rows.map((r) => r.querySelector('.word')?.textContent)).toEqual(['silo', 'abide'])
    expect(rows.map((r) => r.querySelector('.count')?.textContent)).toEqual(['1 500', '750'])
    expect((rows[1].querySelector('.bar') as HTMLElement).style.width).toBe('50%')
  })

  it('the header has the name, the word count and the chapter count', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('h1')?.textContent).toBe('Wool')
    expect(container.querySelector('.dict-meta')?.textContent).toBe('2 000 words, 4 chapters')
  })
})

describe('DictionaryScreen — visibility', () => {
  it('a regular user sees no visibility toggle', async () => {
    const { container } = await render(screen({ role: 'user' }))
    await flush()

    expect(container.querySelector('.dict-visibility')).toBeNull()
  })

  it('an admin sees the current visibility and can toggle it', async () => {
    const { container } = await render(screen({ role: 'admin' }))
    await flush()

    const toggle = container.querySelector<HTMLInputElement>('.dict-visibility input[type="checkbox"]')!
    expect(toggle.checked).toBe(true)

    await click(toggle)
    await flush()

    expect(apiMock.setDictionaryVisibility).toHaveBeenCalledWith(7, false)
    expect(toggle.checked).toBe(false)
  })

  it('reverts the toggle and shows an error when the request fails', async () => {
    apiMock.setDictionaryVisibility.mockRejectedValue(new Error('network error'))
    const { container } = await render(screen({ role: 'admin' }))
    await flush()

    const toggle = container.querySelector<HTMLInputElement>('.dict-visibility input[type="checkbox"]')!
    await click(toggle)
    await flush()

    expect(toggle.checked).toBe(true)
    expect(container.textContent).toContain('network error')
  })
})
