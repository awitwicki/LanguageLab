import { afterEach, describe, expect, it, vi } from 'vitest'
import type {
  ChapterView,
  ReaderBookDto,
  RecentActivity,
  StarredChapter,
  TrainingStarted,
  TrainingStats,
} from '../api/client'
import { MemoryBookStore, type BookStore } from '../reader/bookStore'
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

const nothingRecent: RecentActivity = { exercises: [], sorting: [] }

/// Stubs fetch per path: stats, the starred list and the recent lists always, the review
/// start and unstar when given.
function respond(
  stats: Reply,
  review?: Reply,
  starred: Reply = { status: 200, body: [] },
  unstar: Reply = { status: 204 },
  recent: Reply = { status: 200, body: nothingRecent },
  readerBooks: Reply = { status: 200, body: [] },
) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string, init?: RequestInit) => {
      const reply =
        path === '/api/training/review' && init?.method === 'POST' ? review
        : path === '/api/chapters/starred' ? starred
        : path === '/api/home/recent' ? recent
        : path === '/api/reader/books' ? readerBooks
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
      bookStore={null}
      onOpenBook={() => {}}
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

    expect(fetch).toHaveBeenCalledTimes(3)
    expect(fetch).toHaveBeenCalledWith('/api/chapters/starred', expect.anything())
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.boxes')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })

  it('stats request fails → the home screen is unchanged and shows no error', async () => {
    respondStats(500)

    const { container } = await render(home())
    await flush()

    expect(fetch).toHaveBeenCalledTimes(3)
    expect(fetch).toHaveBeenCalledWith('/api/chapters/starred', expect.anything())
    expect(fetch).toHaveBeenCalledWith('/api/home/recent', expect.anything())
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

  it('the main area sorts the chapter in its book; "Learn" trains it', async () => {
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

const recentActivity: RecentActivity = {
  exercises: [
    {
      trainingId: 31,
      mode: 'newBatch',
      finishedAt: '2026-09-26T10:00:00Z',
      dictionaryId: 7,
      dictionaryName: 'Wool',
      chapter: { id: 11, order: 0, title: 'Holston' },
      correct: 7,
      total: 8,
    },
    {
      trainingId: 32,
      mode: 'review',
      finishedAt: '2026-09-26T09:00:00Z',
      dictionaryId: null,
      dictionaryName: null,
      chapter: null,
      correct: 4,
      total: 4,
    },
  ],
  sorting: [
    {
      dictionaryId: 7,
      dictionaryName: 'Wool',
      chapter: { id: 13, order: 2, title: '' },
      lastSortedAt: '2026-09-26T08:00:00Z',
      total: 80,
      sorted: 30,
    },
    {
      dictionaryId: 9,
      dictionaryName: 'Dune',
      chapter: null,
      lastSortedAt: '2026-09-25T08:00:00Z',
      total: 1000,
      sorted: 250,
    },
  ],
}

/// Only the recent lists matter here, so stats stay untouched and nothing is starred.
function withRecent(activity: RecentActivity, review?: Reply) {
  respond({ status: 200, body: untouched }, review, { status: 200, body: [] }, { status: 204 }, {
    status: 200,
    body: activity,
  })
}

const scopeRows = (container: HTMLElement) => [...container.querySelectorAll('.recent-activity .scope-row')]

const scopeText = (container: HTMLElement) =>
  scopeRows(container).map((r) => [r.querySelector('.scope-title')?.textContent, r.querySelector('.scope-sub')?.textContent])

describe('HomeScreen · pick up where you left off', () => {
  it('names each scope and what is left of it', async () => {
    withRecent(recentActivity)

    const { container } = await render(home())
    await flush()

    expect(scopeText(container)).toEqual([
      ['Wool · Holston', 'Learn · 7 of 8 correct'],
      ['All dictionaries', 'Review · 4 of 4 correct'],
      ['Wool · Chapter 3', '38% sorted · 50 words left'],
      ['Dune', '25% sorted · 750 words left'],
    ])
  })

  it('stays off the screen when there is nothing to pick up', async () => {
    withRecent({ exercises: [], sorting: [] })

    const { container } = await render(home())
    await flush()

    expect(container.querySelector('.recent-activity')).toBeNull()
  })

  /// A batch reopens its start screen rather than replaying the old questions.
  it('repeats a batch by reopening its scope', async () => {
    withRecent(recentActivity)
    const onTrain = vi.fn()

    const { container } = await render(home({ onTrain }))
    await flush()
    await click(scopeRows(container)[0].querySelector('.scope-action')!)

    expect(onTrain).toHaveBeenCalledWith(7, [11], 'Holston')
  })

  /// What is due has moved on since, so a review has to be asked for again.
  it('repeats an all-books review by starting a new one', async () => {
    withRecent(recentActivity, { status: 201, body: reviewStarted })
    const onReview = vi.fn()

    const { container } = await render(home({ onReview }))
    await flush()
    await click(scopeRows(container)[1].querySelector('.scope-action')!)
    await flush()

    expect(fetch).toHaveBeenCalledWith('/api/training/review', expect.objectContaining({ method: 'POST' }))
    expect(onReview).toHaveBeenCalledWith(reviewStarted)
  })

  it('says so when the repeated review has nothing due any more', async () => {
    withRecent(recentActivity, { status: 204 })
    const onReview = vi.fn()

    const { container } = await render(home({ onReview }))
    await flush()
    await click(scopeRows(container)[1].querySelector('.scope-action')!)
    await flush()

    expect(onReview).not.toHaveBeenCalled()
    expect(container.querySelector('.recent-notice')?.textContent).toBe('Nothing to review today.')
  })

  it('sorts a chapter scope, and a whole-book scope with no chapters at all', async () => {
    withRecent(recentActivity)
    const onSort = vi.fn()

    const { container } = await render(home({ onSort }))
    await flush()

    await click(scopeRows(container)[2].querySelector('.scope-action')!)
    expect(onSort).toHaveBeenLastCalledWith(7, [13], 'Chapter 3')

    await click(scopeRows(container)[3].querySelector('.scope-action')!)
    expect(onSort).toHaveBeenLastCalledWith(9, null, 'Whole book')
  })

  /// A failed request must not turn the home screen into an error page.
  it('leaves the block out when the request fails', async () => {
    respond({ status: 200, body: untouched }, undefined, { status: 200, body: [] }, { status: 204 }, { status: 500 })

    const { container } = await render(home())
    await flush()

    expect(container.querySelector('.recent-activity')).toBeNull()
    expect(container.querySelector('.large-title')?.textContent).toBe('Pick a dictionary')
  })
})

const WOOL_HASH = 'a'.repeat(64)

const woolOnServer: ReaderBookDto = {
  fileHash: WOOL_HASH,
  title: 'Wool',
  author: 'Hugh Howey',
  chaptersCount: 30,
  chapterIndex: 6,
  paragraphIndex: 0,
  sentenceIndex: 0,
  progress: 0.45,
  updatedAt: '2026-09-26T10:00:00Z',
  dictionaryId: null,
}

/// A store holding the book's file, as the device that opened it would.
async function storeWithWool(): Promise<BookStore> {
  const store = new MemoryBookStore()

  await store.put(
    { hash: WOOL_HASH, title: 'Wool', author: 'Hugh Howey', fileName: 'wool.fb2', addedAt: '2026-09-01T00:00:00Z' },
    new ArrayBuffer(1),
  )

  return store
}

function withReader(readerBooks: Reply) {
  respond({ status: 200, body: untouched }, undefined, { status: 200, body: [] }, { status: 204 }, {
    status: 200,
    body: nothingRecent,
  }, readerBooks)
}

const readingRow = (container: HTMLElement) =>
  [...container.querySelectorAll('.recent-group')].find((g) => g.querySelector('h3')?.textContent === 'Reading')

describe('HomeScreen · continue reading', () => {
  it('offers the last book with its place in it', async () => {
    withReader({ status: 200, body: [woolOnServer] })

    const { container } = await render(home({ bookStore: await storeWithWool() }))
    await flush()

    const row = readingRow(container)
    expect(row?.querySelector('.scope-title')?.textContent).toBe('Wool')
    expect(row?.querySelector('.scope-sub')?.textContent).toBe('Chapter 7 of 30 · 45 %')
  })

  it('opens the book by its hash', async () => {
    withReader({ status: 200, body: [woolOnServer] })
    const onOpenBook = vi.fn()

    const { container } = await render(home({ bookStore: await storeWithWool(), onOpenBook }))
    await flush()
    await click(readingRow(container)!.querySelector('.scope-action')!)

    expect(onOpenBook).toHaveBeenCalledWith(WOOL_HASH)
  })

  /// A reading book alone is reason enough for the card, with no exercises or sorting behind it.
  it('brings up the card on its own', async () => {
    withReader({ status: 200, body: [woolOnServer] })

    const { container } = await render(home({ bookStore: await storeWithWool() }))
    await flush()

    expect(container.querySelector('.recent-activity')).not.toBeNull()
    expect(container.querySelectorAll('.recent-group')).toHaveLength(1)
  })

  /// The file never leaves the browser that opened it, so there is nothing to continue here.
  it('says nothing about a book read only on another device', async () => {
    withReader({ status: 200, body: [woolOnServer] })

    const { container } = await render(home({ bookStore: new MemoryBookStore() }))
    await flush()

    expect(readingRow(container)).toBeUndefined()
    expect(container.querySelector('.recent-activity')).toBeNull()
  })

  /// The store opens asynchronously; until it does, the row cannot know what is here.
  it('asks for nothing before the store is open', async () => {
    withReader({ status: 200, body: [woolOnServer] })

    const { container } = await render(home({ bookStore: null }))
    await flush()

    expect(fetch).not.toHaveBeenCalledWith('/api/reader/books', expect.anything())
    expect(readingRow(container)).toBeUndefined()
  })
})
