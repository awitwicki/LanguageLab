import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { VerbRow, VerbsProgress } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbStageScreen } from './VerbStageScreen'

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
  learnedPercent: 0,
  stages: [
    { group: 1, title: 'All three forms alike', total: 9, passed: 0, mastery: 0 },
    { group: 2, title: 'First and third alike', total: 3, passed: 0, mastery: 0 },
    { group: 3, title: 'Second and third alike', total: 29, passed: 0, mastery: 0 },
    { group: 4, title: 'All three forms differ', total: 27, passed: 0, mastery: 0 },
  ],
  verbs: [verb('cut', 1), verb('put', 1), verb('come', 2)],
}

describe('VerbStageScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMock.getVerbsProgress.mockResolvedValue(progress)
  })

  it('shows only the verbs of its stage', async () => {
    const { container } = await render(<VerbStageScreen group={1} onStartDrill={() => {}} onBack={() => {}} />)
    await flush()

    expect(container.textContent).toContain('All three forms alike')
    expect(container.querySelectorAll('.verb-table tbody tr')).toHaveLength(2)
    expect(container.textContent).not.toContain('come')
  })

  it('starts ordinary training on the stage', async () => {
    const onStartDrill = vi.fn()
    const { container } = await render(<VerbStageScreen group={1} onStartDrill={onStartDrill} onBack={() => {}} />)
    await flush()

    await click(container.querySelector('.stage-train')!)

    expect(onStartDrill).toHaveBeenCalledWith({ mode: 'batch', group: 1 }, 'All three forms alike')
  })

  it('starts free training on the stage', async () => {
    const onStartDrill = vi.fn()
    const { container } = await render(<VerbStageScreen group={1} onStartDrill={onStartDrill} onBack={() => {}} />)
    await flush()

    await click(container.querySelector('.stage-free')!)

    expect(onStartDrill).toHaveBeenCalledWith({ mode: 'free', group: 1, scope: 'stage' }, 'All three forms alike')
  })

  it('offers the cumulative run from the second stage on', async () => {
    const onStartDrill = vi.fn()
    const { container } = await render(<VerbStageScreen group={2} onStartDrill={onStartDrill} onBack={() => {}} />)
    await flush()

    await click(container.querySelector('.stage-cumulative')!)

    expect(onStartDrill).toHaveBeenCalledWith(
      { mode: 'free', group: 2, scope: 'cumulative' },
      'First and third alike and earlier',
    )
  })

  it('stops offering ordinary training once the whole stage has passed', async () => {
    apiMock.getVerbsProgress.mockResolvedValue({
      ...progress,
      stages: progress.stages.map((s) => (s.group === 1 ? { ...s, passed: s.total } : s)),
    })

    const { container } = await render(<VerbStageScreen group={1} onStartDrill={() => {}} onBack={() => {}} />)
    await flush()

    // The window has nothing unpassed left to serve, so the button would only dead-end.
    expect(container.querySelector<HTMLButtonElement>('.stage-train')!.disabled).toBe(true)
    expect(container.querySelector<HTMLButtonElement>('.stage-free')!.disabled).toBe(false)
  })

  it('has nothing cumulative to offer on the first stage', async () => {
    const { container } = await render(<VerbStageScreen group={1} onStartDrill={() => {}} onBack={() => {}} />)
    await flush()

    expect(container.querySelector('.stage-cumulative')).toBeNull()
  })
})
