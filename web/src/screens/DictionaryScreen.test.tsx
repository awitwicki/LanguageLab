import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DictionaryDetail, LearningProgress, TrainingStarted } from '../api/client'
import { click, flush, render } from '../test/render'
import { DictionaryScreen } from './DictionaryScreen'

const apiMock = vi.hoisted(() => ({
  getDictionary: vi.fn(),
  startReview: vi.fn(),
  setDictionaryStatus: vi.fn(),
  requestPublication: vi.fn(),
  withdrawPublication: vi.fn(),
  deleteDictionary: vi.fn(),
  mark: vi.fn(),
  undo: vi.fn(),
  starChapter: vi.fn(),
  unstarChapter: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

/** Picks an option the way a user would: React hears a select through its change event. */
function choose(select: HTMLSelectElement, value: string) {
  return act(async () => {
    select.value = value
    select.dispatchEvent(new Event('change', { bubbles: true }))
  })
}

const noLearning: LearningProgress = { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }
// notStarted == learnableCount (42) — same as on the server. (9 + 10 + 12 + 60) / (5 · 71) → 26%.
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
    { id: 11, order: 0, title: 'Holston', wordsCount: 300, sortedCount: 150, learnableCount: 42, learning: holstonLearning, dueCount: 0, nextDueAt: null, isStarred: false },
    { id: 12, order: 1, title: '', wordsCount: 100, sortedCount: 100, learnableCount: 0, learning: noLearning, dueCount: 0, nextDueAt: null, isStarred: false },
    // Every word started, five of them due: the row offers a review instead of an exercise.
    { id: 13, order: 2, title: 'Juliette', wordsCount: 80, sortedCount: 80, learnableCount: 0, learning: dueLearning, dueCount: 5, nextDueAt: null, isStarred: false },
    // Every word started, none due until tomorrow: the row says so and waits.
    { id: 14, order: 3, title: 'Lukas', wordsCount: 60, sortedCount: 60, learnableCount: 0, learning: waitingLearning, dueCount: 0, nextDueAt: tomorrow, isStarred: false },
  ],
  topWords: [
    { wordPairId: 1, word: 'silo', frequency: 1500 },
    { wordPairId: 2, word: 'abide', frequency: 750 },
  ],
  status: 'published',
}

const reviewStarted: TrainingStarted = { trainingId: 9, mode: 'review', words: [], totalQuestions: 12 }

function screen(overrides: Partial<Parameters<typeof DictionaryScreen>[0]> = {}) {
  return (
    <DictionaryScreen
      id={7}
      role="user"
      onSort={() => {}}
      onTrain={() => {}}
      onReview={() => {}}
      onDeleted={() => {}}
      {...overrides}
    />
  )
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.getDictionary.mockResolvedValue(detail)
  apiMock.startReview.mockResolvedValue(reviewStarted)
  apiMock.setDictionaryStatus.mockResolvedValue(null)
  apiMock.requestPublication.mockResolvedValue(null)
  apiMock.withdrawPublication.mockResolvedValue(null)
  apiMock.deleteDictionary.mockResolvedValue(null)
  apiMock.mark.mockResolvedValue(null)
  apiMock.undo.mockResolvedValue(null)
  apiMock.starChapter.mockResolvedValue(null)
  apiMock.unstarChapter.mockResolvedValue(null)
})

describe('DictionaryScreen — chapters', () => {
  it('clicking a chapter title sorts only it; "Learn" trains only it', async () => {
    const onSort = vi.fn()
    const onTrain = vi.fn()
    const { container } = await render(screen({ onSort, onTrain }))
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    expect(rows).toHaveLength(4)
    expect(rows[0].querySelector('.chapter-title')?.textContent).toBe('Holston')
    expect(rows[0].querySelector('.chapter-sub')?.textContent).toBe('300 words · 50% sorted · 42 to learn')
    expect(container.querySelector('input[type="checkbox"]')).toBeNull()

    await click(rows[0].querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith([11], 'Holston')

    await click(rows[0].querySelector('.chapter-train')!)
    expect(onTrain).toHaveBeenCalledWith([11], 'Holston')
  })

  it('an untitled chapter is numbered; "Learn" is disabled with nothing to learn', async () => {
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
    expect(scale.querySelector('.leitner-percent')?.textContent).toBe('26% learned')
    // The scale sits outside the sort button: its aria-label must not pollute the button's name.
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
    expect(juliette.querySelector('.chapter-sub')?.textContent).toBe('80 words · all sorted · 5 to review')

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
    expect(lukas.querySelector('.chapter-sub')?.textContent).toBe('60 words · all sorted · 8 in progress · next review tomorrow')

    const button = lukas.querySelector<HTMLButtonElement>('.chapter-train')!
    expect(button.textContent).toBe('Learn')
    expect(button.disabled).toBe(true)
  })

  it('a chapter with new words keeps "Learn" even when some of its words are due', async () => {
    apiMock.getDictionary.mockResolvedValue({
      ...detail,
      chapters: [{ ...detail.chapters[0], dueCount: 3 }],
    })
    const { container } = await render(screen())
    await flush()

    expect(row(container, 0).querySelector('.chapter-sub')?.textContent).toBe('300 words · 50% sorted · 42 to learn')
    expect(row(container, 0).querySelector('.chapter-train')?.textContent).toBe('Learn')
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

  it('"Learn new words" → onTrain(null, "Whole book")', async () => {
    const onTrain = vi.fn()
    const { container } = await render(screen({ onTrain }))
    await flush()

    const start = buttons(container).find((b) => b.textContent?.includes('Learn new words'))!
    expect(start.disabled).toBe(false)

    await click(start)

    expect(onTrain).toHaveBeenCalledWith(null, 'Whole book')
  })

  it('with nothing to learn, "Learn new words" is disabled and a hint is shown', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, learnableCount: 0 })
    const { container } = await render(screen())
    await flush()

    expect(buttons(container).find((b) => b.textContent?.includes('Learn new words'))!.disabled).toBe(true)
    expect(container.textContent).toContain('No words to learn yet')
  })

  it('"Review (3)" appears when dueCount > 0 and starts a review scoped to the book', async () => {
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Review (3)')!)
    await flush()

    expect(apiMock.startReview).toHaveBeenCalledWith({ dictionaryId: 7, chapterIds: null })
    expect(onReview).toHaveBeenCalledWith(reviewStarted, undefined)
  })

  // The header's button is the book's review; a chapter row may still offer its own.
  it('no header review button without due words', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, dueCount: 0 })
    const { container } = await render(screen())
    await flush()

    const header = [...container.querySelectorAll<HTMLButtonElement>('.dict-actions button')]
    expect(header.find((b) => b.textContent?.startsWith('Review'))).toBeUndefined()
  })

  it('the header stacks the sorted bar and the learned scale on one grid, with the caption below', async () => {
    const { container } = await render(screen())
    await flush()

    const header = container.querySelector('.dict-header .scope-progress')!
    expect([...header.querySelectorAll('.scope-label')].map((l) => l.textContent)).toEqual(['Sorted', 'Learned'])
    expect(header.querySelector('.scope-value')?.textContent).toBe('25%')
    expect(header.querySelector('.leitner-percent')?.textContent).toBe('26%')
    // The caption explains the book's scale once; the identical chapter scales below don't repeat it.
    expect(header.querySelector('.scope-caption:last-child')?.textContent).toContain('box 1')
    expect(container.querySelector('.chapter-learning .leitner-caption')).toBeNull()

    apiMock.getDictionary.mockResolvedValue({ ...detail, learning: noLearning })
    const empty = await render(screen())
    await flush()

    expect(empty.container.querySelector('.dict-header .leitner')).toBeNull()
  })

  it('a fully sorted book hides "Sort the whole book" and makes "Learn new words" the primary action', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, sortedCount: detail.wordsCount })
    const { container } = await render(screen())
    await flush()

    expect(buttons(container).find((b) => b.textContent?.includes('Sort the whole book'))).toBeUndefined()
    const learn = buttons(container).find((b) => b.textContent?.includes('Learn new words'))!
    expect(learn.classList.contains('btn-primary')).toBe(true)
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

  it('an admin sees the current status and can change it', async () => {
    const { container } = await render(screen({ role: 'admin' }))
    await flush()

    const select = container.querySelector<HTMLSelectElement>('.dict-visibility select')!
    expect(select.value).toBe('published')

    await choose(select, 'private')
    await flush()

    expect(apiMock.setDictionaryStatus).toHaveBeenCalledWith(7, 'private')
    expect(select.value).toBe('private')
  })

  it('reverts the status and shows an error when the request fails', async () => {
    apiMock.setDictionaryStatus.mockRejectedValue(new Error('network error'))
    const { container } = await render(screen({ role: 'admin' }))
    await flush()

    const select = container.querySelector<HTMLSelectElement>('.dict-visibility select')!
    await choose(select, 'private')
    await flush()

    expect(select.value).toBe('published')
    expect(container.textContent).toContain('network error')
  })
})

describe('DictionaryScreen — sharing', () => {
  it('lets the owner offer a private dictionary for publication', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, status: 'private' })
    const { container } = await render(screen({ role: 'user' }))
    await flush()

    expect(container.textContent).toContain('Only you can see this dictionary')

    await click(buttons(container).find((b) => b.textContent === 'Submit for review')!)
    await flush()

    expect(apiMock.requestPublication).toHaveBeenCalledWith(7)
  })

  it('shows a pending dictionary as waiting, with a way to take it back', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, status: 'pending' })
    const { container } = await render(screen({ role: 'user' }))
    await flush()

    expect(container.textContent).toContain('Waiting for review')
    expect(buttons(container).find((b) => b.textContent === 'Withdraw')).toBeTruthy()
  })
})

describe('DictionaryScreen — delete', () => {
  it('a regular user sees no delete button', async () => {
    const { container } = await render(screen({ role: 'user' }))
    await flush()

    expect(container.querySelector('.delete-dictionary')).toBeNull()
  })

  it('an admin deletes after confirming, and the app is told', async () => {
    const onDeleted = vi.fn()
    const { container } = await render(screen({ role: 'admin', onDeleted }))
    await flush()

    const button = container.querySelector<HTMLButtonElement>('.delete-dictionary')!
    expect(button.textContent).toBe('Delete dictionary')
    expect(apiMock.deleteDictionary).not.toHaveBeenCalled()

    await click(button)
    expect(button.textContent).toBe('Confirm deletion')
    expect(container.querySelector('.delete-warning')).not.toBeNull()
    expect(apiMock.deleteDictionary).not.toHaveBeenCalled()

    await click(button)
    await flush()

    expect(apiMock.deleteDictionary).toHaveBeenCalledWith(7)
    expect(onDeleted).toHaveBeenCalledTimes(1)
  })

  it('un-arms and shows an error when the delete request fails', async () => {
    apiMock.deleteDictionary.mockRejectedValue(new Error('network error'))
    const onDeleted = vi.fn()
    const { container } = await render(screen({ role: 'admin', onDeleted }))
    await flush()

    const button = container.querySelector<HTMLButtonElement>('.delete-dictionary')!
    await click(button)
    await click(button)
    await flush()

    expect(onDeleted).not.toHaveBeenCalled()
    expect(button.textContent).toBe('Delete dictionary')
    expect(container.textContent).toContain('network error')
  })
})

describe('DictionaryScreen — exclude word', () => {
  const words = (container: HTMLElement) =>
    [...container.querySelectorAll('.top-word')].map((r) => r.querySelector('.word')?.textContent)

  it('excludes a word, refreshes the list, and offers Undo', async () => {
    const refreshed: DictionaryDetail = {
      ...detail,
      topWords: [
        { wordPairId: 2, word: 'abide', frequency: 750 },
        { wordPairId: 3, word: 'cleaning', frequency: 200 },
      ],
    }
    apiMock.getDictionary.mockResolvedValueOnce(detail).mockResolvedValueOnce(refreshed)
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector<HTMLButtonElement>('.top-word .exclude-word')!)
    await flush()

    expect(apiMock.mark).toHaveBeenCalledWith(1, 'excluded')
    expect(apiMock.getDictionary).toHaveBeenCalledTimes(2)
    expect(words(container)).toEqual(['abide', 'cleaning'])
    expect(container.querySelector('.top-words-notice')?.textContent).toContain('Excluded "silo"')
  })

  it('Undo reverses the exclusion and refreshes the list again', async () => {
    const withoutSilo: DictionaryDetail = { ...detail, topWords: [{ wordPairId: 2, word: 'abide', frequency: 750 }] }
    apiMock.getDictionary.mockResolvedValueOnce(detail).mockResolvedValueOnce(withoutSilo).mockResolvedValueOnce(detail)
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector<HTMLButtonElement>('.top-word .exclude-word')!)
    await flush()
    await click(container.querySelector<HTMLButtonElement>('.top-words-notice .btn')!)
    await flush()

    expect(apiMock.undo).toHaveBeenCalledTimes(1)
    expect(apiMock.getDictionary).toHaveBeenCalledTimes(3)
    expect(container.querySelector('.top-words-notice')).toBeNull()
    expect(words(container)).toEqual(['silo', 'abide'])
  })

  it('shows an inline error and keeps the word when excluding fails', async () => {
    apiMock.mark.mockRejectedValue(new Error('network error'))
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector<HTMLButtonElement>('.top-word .exclude-word')!)
    await flush()

    expect(apiMock.getDictionary).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.top-words-notice')).toBeNull()
    expect(container.textContent).toContain('network error')
    expect(words(container)).toEqual(['silo', 'abide'])
  })
})

describe('DictionaryScreen — starred', () => {
  const headings = (container: HTMLElement) => [...container.querySelectorAll('h2')].map((h) => h.textContent)
  const withStar = { ...detail, chapters: detail.chapters.map((c) => (c.id === 13 ? { ...c, isStarred: true } : c)) }

  it('nothing starred → no "Starred" section, one chapter list', async () => {
    const { container } = await render(screen())
    await flush()

    expect(headings(container)).toEqual(['Chapters', 'Most frequent words', 'Sharing'])
    expect(container.querySelectorAll('.chapter-list')).toHaveLength(1)
  })

  it('starred chapters sit in a compact list above the chapters', async () => {
    apiMock.getDictionary.mockResolvedValue(withStar)
    const { container } = await render(screen())
    await flush()

    expect(headings(container)).toEqual(['Starred', 'Chapters', 'Most frequent words', 'Sharing'])

    const lists = container.querySelectorAll('.chapter-list')
    expect(lists).toHaveLength(2)

    const starredRows = lists[0].querySelectorAll('.chapter-row')
    expect(starredRows).toHaveLength(1)
    expect(starredRows[0].querySelector('.chapter-title')?.textContent).toBe('Juliette')
    expect(starredRows[0].classList.contains('compact')).toBe(true)
    expect(starredRows[0].querySelector('.chapter-learning')).toBeNull()
    expect(starredRows[0].querySelector('.chapter-train')?.textContent).toBe('Review')

    // The full list still has every chapter, with its scale, and the same chapter starred there too.
    expect(lists[1].querySelectorAll('.chapter-row')).toHaveLength(4)
    expect(lists[1].querySelectorAll('.chapter-row')[2].querySelector('.chapter-star')?.getAttribute('aria-pressed')).toBe('true')
  })

  it('a starred row in the mini-list sorts and reviews its chapter like the full list does', async () => {
    apiMock.getDictionary.mockResolvedValue(withStar)
    const onSort = vi.fn()
    const onReview = vi.fn()
    const { container } = await render(screen({ onSort, onReview }))
    await flush()

    const row = container.querySelectorAll('.chapter-list')[0].querySelector('.chapter-row')!
    await click(row.querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith([13], 'Juliette')

    await click(row.querySelector('.chapter-train')!)
    await flush()
    expect(apiMock.startReview).toHaveBeenCalledWith({ dictionaryId: 7, chapterIds: [13] })
    expect(onReview).toHaveBeenCalledWith(reviewStarted, 'Juliette')
  })

  it('starring sends PUT and the chapter joins the mini-list; unstarring there removes it', async () => {
    const { container } = await render(screen())
    await flush()

    const holston = container.querySelectorAll('.chapter-row')[0]
    await click(holston.querySelector('.chapter-star')!)
    await flush()

    expect(apiMock.starChapter).toHaveBeenCalledWith(11)
    expect(headings(container)[0]).toBe('Starred')
    const mini = container.querySelectorAll('.chapter-list')[0]
    expect(mini.querySelector('.chapter-title')?.textContent).toBe('Holston')
    expect(mini.querySelector('.chapter-star')?.getAttribute('aria-pressed')).toBe('true')

    await click(mini.querySelector('.chapter-star')!)
    await flush()

    expect(apiMock.unstarChapter).toHaveBeenCalledWith(11)
    expect(headings(container)).toEqual(['Chapters', 'Most frequent words', 'Sharing'])
    expect(container.querySelectorAll('.chapter-row')[0].querySelector('.chapter-star')?.getAttribute('aria-pressed')).toBe('false')
  })

  it('a failed star request flips the star back and shows the error', async () => {
    apiMock.starChapter.mockRejectedValue(new Error('PUT /api/chapters/11/star → 404'))
    const { container } = await render(screen())
    await flush()

    await click(container.querySelectorAll('.chapter-row')[0].querySelector('.chapter-star')!)
    await flush()

    expect(headings(container)).toEqual(['Chapters', 'Most frequent words', 'Sharing'])
    expect(container.querySelectorAll('.chapter-row')[0].querySelector('.chapter-star')?.getAttribute('aria-pressed')).toBe('false')
    expect(container.querySelector('.error')?.textContent).toBe('PUT /api/chapters/11/star → 404')
  })
})
