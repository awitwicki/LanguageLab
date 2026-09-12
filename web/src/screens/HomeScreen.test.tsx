import { afterEach, describe, expect, it, vi } from 'vitest'
import type { TrainingStats } from '../api/client'
import { flush, render } from '../test/render'
import { HomeScreen } from './HomeScreen'

const active: TrainingStats = { boxCounts: [12, 6, 0, 3, 1], learned: 40, known: 500, due: 7, correct: 120, wrong: 15 }
const untouched: TrainingStats = { boxCounts: [0, 0, 0, 0, 0], learned: 0, known: 0, due: 0, correct: 0, wrong: 0 }

function respondStats(status: number, body?: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string) => {
      expect(path).toBe('/api/training/stats')

      return Promise.resolve({ ok: status < 300, status, json: () => Promise.resolve(body) } as Response)
    }),
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('HomeScreen', () => {
  it('shows in-progress, learned and due-today tiles plus the box histogram once there is any Leitner activity', async () => {
    respondStats(200, active)

    const { container } = await render(<HomeScreen hasDictionaries onImport={() => {}} />)
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

  it('no activity yet → the placeholder alone, no zero-filled tiles', async () => {
    respondStats(200, untouched)

    const { container } = await render(<HomeScreen hasDictionaries onImport={() => {}} />)
    await flush()

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.boxes')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })

  it('stats request fails → the home screen is unchanged and shows no error', async () => {
    respondStats(500)

    const { container } = await render(<HomeScreen hasDictionaries onImport={() => {}} />)
    await flush()

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.stat-tile')).toBeNull()
    expect(container.querySelector('.error')).toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('Pick a dictionary')
  })
})
