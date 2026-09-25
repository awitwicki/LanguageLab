import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { VerbRow, VerbsProgress } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbsScreen } from './VerbsScreen'

const apiMock = vi.hoisted(() => ({ getVerbsProgress: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function verb(v1: string, group: number): VerbRow {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `пер-${v1}`,
    group,
    mastery: 0,
    streak: 0,
    passed: false,
    answers: 0,
  }
}

const progress: VerbsProgress = {
  learnedPercent: 12,
  stages: [
    { group: 1, title: 'All three forms alike', total: 9, passed: 2, mastery: 0.3 },
    { group: 2, title: 'First and third alike', total: 3, passed: 0, mastery: 0 },
    { group: 3, title: 'Second and third alike', total: 29, passed: 6, mastery: 0.5 },
    { group: 4, title: 'All three forms differ', total: 27, passed: 0, mastery: 0 },
  ],
  verbs: [verb('cut', 1), verb('come', 2), verb('buy', 3), verb('go', 4)],
}

describe('VerbsScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMock.getVerbsProgress.mockResolvedValue(progress)
  })

  it('shows one tile per stage with its progress', async () => {
    const { container } = await render(<VerbsScreen onOpenStage={() => {}} onStartDrill={() => {}} />)
    await flush()

    const tiles = container.querySelectorAll('.stage-tile')

    expect(tiles).toHaveLength(4)
    expect(tiles[0].textContent).toContain('All three forms alike')
    expect(tiles[0].textContent).toContain('2 of 9')
  })

  it('opens a stage', async () => {
    const onOpenStage = vi.fn()
    const { container } = await render(<VerbsScreen onOpenStage={onOpenStage} onStartDrill={() => {}} />)
    await flush()

    await click(container.querySelectorAll('.stage-tile button')[0])

    expect(onOpenStage).toHaveBeenCalledWith(1)
  })

  it('starts free training over the whole catalog', async () => {
    const onStartDrill = vi.fn()
    const { container } = await render(<VerbsScreen onOpenStage={() => {}} onStartDrill={onStartDrill} />)
    await flush()

    await click(container.querySelector('.verbs-free')!)

    expect(onStartDrill).toHaveBeenCalledWith({ mode: 'free', scope: 'all' }, 'All verbs')
  })

  it('lists every verb of the catalog underneath', async () => {
    const { container } = await render(<VerbsScreen onOpenStage={() => {}} onStartDrill={() => {}} />)
    await flush()

    expect(container.querySelectorAll('.verb-table')).toHaveLength(4)
    expect(container.querySelectorAll('.verb-table tbody tr')).toHaveLength(4)
  })
})
