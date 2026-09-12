import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionStarted, VerbsProgress } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbGroupScreen } from './VerbGroupScreen'

const apiMock = vi.hoisted(() => ({ getVerbsProgress: vi.fn(), startVerbSession: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function verb(v1: string, state: VerbsProgress['groups'][number]['families'][number]['verbs'][number]['state'] = 'new') {
  return { v1, v2: v1, v3: v1, translation: v1, state, flagged: false }
}

const progress: VerbsProgress = {
  learnedPercent: 0,
  groups: [
    {
      group: 1,
      title: 'All three forms alike',
      total: 9,
      learned: 0,
      unlocked: true,
      families: [
        {
          key: 'same',
          title: 'All three forms alike',
          status: 'available',
          total: 9,
          learned: 0,
          verbs: [verb('cut'), verb('put')],
        },
      ],
    },
    {
      group: 2,
      title: 'First and third alike',
      total: 3,
      learned: 0,
      unlocked: false,
      families: [
        { key: 'back', title: 'Third form goes back to the first', status: 'locked', total: 3, learned: 0, verbs: [verb('come')] },
      ],
    },
  ],
  mixedAvailable: false,
  errorsAvailable: false,
  activeSession: null,
}

beforeEach(() => {
  apiMock.getVerbsProgress.mockReset()
  apiMock.startVerbSession.mockReset()
  apiMock.getVerbsProgress.mockResolvedValue(progress)
})

describe('VerbGroupScreen', () => {
  it('shows the group families with Start for an untouched one', async () => {
    const { container } = await render(<VerbGroupScreen group={1} onBack={() => {}} onStartSession={() => {}} />)
    await flush()

    const node = container.querySelector('.family-node')!
    expect(node.textContent).toContain('All three forms alike')
    expect(node.querySelector('.btn')?.textContent).toBe('Start')
  })

  it('shows a locked family with a disabled button and a hint', async () => {
    const { container } = await render(<VerbGroupScreen group={2} onBack={() => {}} onStartSession={() => {}} />)
    await flush()

    const node = container.querySelector('.family-node')!
    expect(node.querySelector('.btn')?.hasAttribute('disabled')).toBe(true)
    expect(node.textContent).toContain('Locked')
  })

  it('shows Continue once a family has started progress', async () => {
    const started = {
      ...progress,
      groups: [
        {
          ...progress.groups[0],
          families: [{ ...progress.groups[0].families[0], verbs: [verb('cut', 'learning1'), verb('put')] }],
        },
        progress.groups[1],
      ],
    }
    apiMock.getVerbsProgress.mockResolvedValue(started)

    const { container } = await render(<VerbGroupScreen group={1} onBack={() => {}} onStartSession={() => {}} />)
    await flush()

    expect(container.querySelector('.family-node .btn')?.textContent).toBe('Continue')
  })

  it('shows Practise again for a done family', async () => {
    const done = {
      ...progress,
      groups: [{ ...progress.groups[0], families: [{ ...progress.groups[0].families[0], status: 'done' as const }] }, progress.groups[1]],
    }
    apiMock.getVerbsProgress.mockResolvedValue(done)

    const { container } = await render(<VerbGroupScreen group={1} onBack={() => {}} onStartSession={() => {}} />)
    await flush()

    expect(container.querySelector('.family-node .btn')?.textContent).toBe('Practise again')
  })

  it('starts a learn session for the family and reports it', async () => {
    const started: SessionStarted = { id: 3, mode: 'learn', group: 1, family: 'same', title: 'All three forms alike', total: 16 }
    apiMock.startVerbSession.mockResolvedValue(started)
    const onStartSession = vi.fn()

    const { container } = await render(<VerbGroupScreen group={1} onBack={() => {}} onStartSession={onStartSession} />)
    await flush()

    await click(container.querySelector('.family-node .btn')!)
    await flush()

    expect(apiMock.startVerbSession).toHaveBeenCalledWith({ mode: 'learn', family: 'same' })
    expect(onStartSession).toHaveBeenCalledWith(started)
  })

  it('toggles a verb table for the family', async () => {
    const { container } = await render(<VerbGroupScreen group={1} onBack={() => {}} onStartSession={() => {}} />)
    await flush()

    expect(container.querySelector('table')).toBeNull()

    await click(container.querySelector('.family-verbs-toggle')!)

    expect(container.querySelector('table')?.textContent).toContain('cut')
  })

  it('calls onBack', async () => {
    const onBack = vi.fn()
    const { container } = await render(<VerbGroupScreen group={1} onBack={onBack} onStartSession={() => {}} />)
    await flush()

    await click(container.querySelector('.btn-quiet')!)
    expect(onBack).toHaveBeenCalledTimes(1)
  })
})
