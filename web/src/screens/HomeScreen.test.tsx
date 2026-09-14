import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ChapterView, StarredChapter, TrainingStarted, TrainingStats } from '../api/client'
import { click, flush, render } from '../test/render'
import { HomeScreen } from './HomeScreen'

const active: TrainingStats = {
  boxCounts: [12, 6, 0, 3, 1],
  learned: 40,
  known: 500,
  unknown: 2797,
  excluded: 31,
  due: 7,
  correct: 120,
  wrong: 15,
}
const untouched: TrainingStats = {
  boxCounts: [0, 0, 0, 0, 0],
  learned: 0,
  known: 0,
  unknown: 0,
  excluded: 0,
  due: 0,
  correct: 0,
  wrong: 0,
}
const reviewStarted: TrainingStarted = { trainingId: 9, mode: 'review', words: [], totalQuestions: 12 }

const holston: ChapterView = {
  id: 11,
  order: 0,
  title: 'Holston',
  wordsCount: 300,
  sortedCount: 150,
  learnableCount: 42,
  learning: { notStarted: 42, boxes: [9, 5, 0, 3, 0], learned: 12, total: 71 },
  dueCount: 0,
  nextDueAt: null,
  isStarred: true,
}
const juliette: ChapterView = {
  ...holston,
  id: 13,
  order: 2,
  title: 'Juliette',
  wordsCount: 80,
  sortedCount: 80,
  learnableCount: 0,
  learning: { notStarted: 0, boxes: [5, 4, 0, 0, 0], learned: 1, total: 10 },
  dueCount: 5,
}
// Already in the API's order: book name, then chapter order.
const starredChapters: StarredChapter[] = [
  { dictionaryId: 9, dictionaryName: 'Dune', chapter: { ...holston, id: 21, title: 'Arrakis' } },
  { dictionaryId: 7, dictionaryName: 'Wool', chapter: holston },
  { dictionaryId: 7, dictionaryName: 'Wool', chapter: juliette },
]

type Reply = { status: number; body?: unknown }

/// Stubs fetch per path: stats and the starred list always, the review start and unstar when given.
function respond(
  stats: Reply,
  review?: Reply,
  starred: Reply = { status: 200, body: [] },
  unstar: Reply = { status: 204 },
) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string, init?: RequestInit) => {
      const reply =
        path === '/api/training/review' && init?.method === 'POST' ? review
        : path === '/api/chapters/starred' ? starred
        : path.startsWith('/api/chapters/') && init?.method === 'DELETE' ? unstar
        : stats
      expect(reply, `unexpected request ${init?.method ?? 'GET'} ${path}`).toBeDefined()

      const { status, body } = reply!
      return Promise.resolve({ ok: status < 300, status, json: () => Promise.resolve(body) } as Response)
    }),
  )
}

function respondStats(status: number, body?: unknown) {
  respond({ status, body })
}

function home(overrides: Partial<Parameters<typeof HomeScreen>[0]> = {}) {
  return (
    <HomeScreen
      hasDictionaries
      onImport={() => {}}
      onReview={() => {}}
      onSort={() => {}}
      onTrain={() => {}}
      onChapterReview={() => {}}
      {...overrides}
    />
  )
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

function tiles(container: HTMLElement) {
  return [...container.querySelectorAll('.stat-tile')].map((t) => [
    t.querySelector('.stat-label')?.textContent,
    t.querySelector('.stat-value')?.textContent,
  ])
}

afterEach(() => vi.unstubAllGlobals())

describe('HomeScreen', () => {
  it('shows the shelf tiles, then the Leitner tiles and the box histogram once there is any activity', async () => {
    respondStats(200, active)

    const { container } = await render(home())
    await flush()

    expect(tiles(container)).toEqual([
      ['Known', '500'],
      ['To learn', '2 797'],
      ['Excluded', '31'],
      ['In progress', '22'],
      ['Learned', '40'],
      ['Due today', '7'],
    ])
    expect(container.querySelectorAll('.boxes-bar')).toHaveLength(5)
    expect(container.querySelector('.welcome-hint')?.textContent).toContain('sidebar')
  })

  it('sorted but never trained → the shelf tiles alone, no Leitner tiles or histogram', async () => {
    respondStats(200, { ...untouched, known: 12, unknown: 3 })

    const { container } = await render(home())
    await flush()

    expect(tiles(container)).toEqual([
      ['Known', '12'],
      ['To learn', '3'],
      ['Excluded', '0'],
    ])
    expect(container.querySelector('.boxes')).toBeNull()
  })

  it('"Review" next to the due-today tile starts a review across every book and hands it back', async () => {
    respond({ status: 200, body: active }, { status: 201, body: reviewStarted })
    const onReview = vi.fn()

    const { container } = await render(home({ onReview }))
    await flush()
    await click(buttons(container).find((b) => b.textContent === 'Review')!)
    await flush()

    // No body: the server reviews everything due, not one book's share.
    expect(fetch).toHaveBeenCalledWith('/api/training/review', expect.objectContaining({ method: 'POST', body: undefined }))
    expect(onReview).toHaveBeenCalledWith(reviewStarted)
  })

  it('nothing due → no "Review" button', async () => {
    respondStats(200, { ...active, due: 0 })

    const { container } = await render(home())
    await flush()

    expect(buttons(container).find((b) => b.textContent === 'Review')).toBeUndefined()
  })

  it('a review with no words (204) → a notice, onReview not called', async () => {
    respond({ status: 200, body: active }, { status: 204 })
    const onReview = vi.fn()

    const { container } = await render(home({ onReview }))
    await flush()
    await click(buttons(container).find((b) => b.textContent === 'Review')!)
    await flush()

    expect(container.textContent).toContain('Nothing to review today.')
    expect(onReview).not.toHaveBeenCalled()
  })

  it('no activity yet → the placeholder alone, no zero-filled tiles', async () => {
    respondStats(200, untouched)

    const { container } = await render(home())
    await flush()

    expect(fetch).toHaveBeenCalledTimes(2)
    expect(fetch).toHaveBeenCalledWith('/api/chapters/starred', expect.anything())
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.boxes')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })

  it('stats request fails → the home screen is unchanged and shows no error', async () => {
    respondStats(500)

    const { container } = await render(home())
    await flush()

    expect(fetch).toHaveBeenCalledTimes(2)
    expect(fetch).toHaveBeenCalledWith('/api/chapters/starred', expect.anything())
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.error')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })
})

describe('HomeScreen — starred chapters', () => {
  const books = (container: HTMLElement) => [...container.querySelectorAll('.starred-book h3')].map((h) => h.textContent)
  const rows = (container: HTMLElement) => [...container.querySelectorAll('.starred-chapters .chapter-row')]

  it('lists starred chapters grouped by book, as compact rows, above the placeholder', async () => {
    respond({ status: 200, body: untouched }, undefined, { status: 200, body: starredChapters })

    const { container } = await render(home())
    await flush()

    expect(books(container)).toEqual(['Dune', 'Wool'])
    expect(rows(container).map((r) => r.querySelector('.chapter-title')?.textContent)).toEqual(['Arrakis', 'Holston', 'Juliette'])
    expect(rows(container).every((r) => r.classList.contains('compact'))).toBe(true)
    expect(container.querySelector('.chapter-learning')).toBeNull()

    const section = container.querySelector('.starred-chapters')!
    expect(section.compareDocumentPosition(container.querySelector('h1')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('nothing starred → no section', async () => {
    respond({ status: 200, body: untouched })

    const { container } = await render(home())
    await flush()

    expect(container.querySelector('.starred-chapters')).toBeNull()
  })

  it('the starred request fails → no section and no error', async () => {
    respond({ status: 200, body: untouched }, undefined, { status: 500 })

    const { container } = await render(home())
    await flush()

    expect(container.querySelector('.starred-chapters')).toBeNull()
    expect(container.querySelector('.error')).toBeNull()
  })

  it('the main area sorts the chapter in its book; "Exercise" trains it', async () => {
    respond({ status: 200, body: untouched }, undefined, { status: 200, body: starredChapters })
    const onSort = vi.fn()
    const onTrain = vi.fn()

    const { container } = await render(home({ onSort, onTrain }))
    await flush()

    await click(rows(container)[1].querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith(7, [11], 'Holston')

    await click(rows(container)[1].querySelector('.chapter-train')!)
    expect(onTrain).toHaveBeenCalledWith(7, [11], 'Holston')
  })

  it('"Review" on a starred chapter starts a review scoped to it and hands back the book and chapter', async () => {
    respond({ status: 200, body: untouched }, { status: 201, body: reviewStarted }, { status: 200, body: starredChapters })
    const onChapterReview = vi.fn()

    const { container } = await render(home({ onChapterReview }))
    await flush()

    const review = rows(container)[2].querySelector<HTMLButtonElement>('.chapter-train')!
    expect(review.textContent).toBe('Review')
    await click(review)
    await flush()

    expect(fetch).toHaveBeenCalledWith(
      '/api/training/review',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ dictionaryId: 7, chapterIds: [13] }) }),
    )
    expect(onChapterReview).toHaveBeenCalledWith(reviewStarted, 7, 'Juliette')
  })

  it('a chapter review with no words (204) → a notice under the section', async () => {
    respond({ status: 200, body: untouched }, { status: 204 }, { status: 200, body: starredChapters })
    const onChapterReview = vi.fn()

    const { container } = await render(home({ onChapterReview }))
    await flush()
    await click(rows(container)[2].querySelector('.chapter-train')!)
    await flush()

    expect(container.querySelector('.starred-notice')?.textContent).toBe('Nothing to review today.')
    expect(onChapterReview).not.toHaveBeenCalled()
  })

  it('unstarring sends DELETE and drops the row — and the book once it is empty', async () => {
    respond({ status: 200, body: untouched }, undefined, { status: 200, body: starredChapters })

    const { container } = await render(home())
    await flush()

    await click(rows(container)[0].querySelector('.chapter-star')!)
    await flush()

    expect(fetch).toHaveBeenCalledWith('/api/chapters/21/star', expect.objectContaining({ method: 'DELETE' }))
    expect(books(container)).toEqual(['Wool'])
    expect(rows(container)).toHaveLength(2)

    await click(rows(container)[0].querySelector('.chapter-star')!)
    await flush()
    await click(rows(container)[0].querySelector('.chapter-star')!)
    await flush()

    expect(container.querySelector('.starred-chapters')).toBeNull()
  })

  it('a failed unstar keeps the row and shows the error', async () => {
    respond({ status: 200, body: untouched }, undefined, { status: 200, body: starredChapters }, { status: 500 })

    const { container } = await render(home())
    await flush()
    await click(rows(container)[0].querySelector('.chapter-star')!)
    await flush()

    expect(rows(container)).toHaveLength(3)
    expect(container.querySelector('.starred-notice.error')?.textContent).toBe('Error: DELETE /api/chapters/21/star → 500')
  })
})
