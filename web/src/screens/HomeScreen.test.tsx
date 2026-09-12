import { afterEach, describe, expect, it, vi } from 'vitest'
import type { TrainingStarted, TrainingStats } from '../api/client'
import { click, flush, render } from '../test/render'
import { HomeScreen } from './HomeScreen'

const active: TrainingStats = { boxCounts: [12, 6, 0, 3, 1], learned: 40, known: 500, due: 7, correct: 120, wrong: 15 }
const untouched: TrainingStats = { boxCounts: [0, 0, 0, 0, 0], learned: 0, known: 0, due: 0, correct: 0, wrong: 0 }
const reviewStarted: TrainingStarted = { trainingId: 9, mode: 'review', words: [], totalQuestions: 12 }

type Reply = { status: number; body?: unknown }

/// Stubs fetch per path: the stats request always, the review start only when given.
function respond(stats: Reply, review?: Reply) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string, init?: RequestInit) => {
      const reply = path === '/api/training/review' && init?.method === 'POST' ? review : stats
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
  return <HomeScreen hasDictionaries onImport={() => {}} onReview={() => {}} {...overrides} />
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

afterEach(() => vi.unstubAllGlobals())

describe('HomeScreen', () => {
  it('shows in-progress, learned and due-today tiles plus the box histogram once there is any Leitner activity', async () => {
    respondStats(200, active)

    const { container } = await render(home())
    await flush()

    const tiles = [...container.querySelectorAll('.stat-tile')].map((t) => [
      t.querySelector('.stat-label')?.textContent,
      t.querySelector('.stat-value')?.textContent,
    ])
    expect(tiles).toEqual([
      ['In progress', '22'],
      ['Learned', '40'],
      ['Due today', '7'],
    ])
    expect(container.querySelectorAll('.boxes-bar')).toHaveLength(5)
    expect(container.querySelector('.welcome-hint')?.textContent).toContain('sidebar')
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

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.boxes')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })

  it('stats request fails → the home screen is unchanged and shows no error', async () => {
    respondStats(500)

    const { container } = await render(home())
    await flush()

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.error')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })
})
