import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionStarted, VerbsProgress } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbsScreen } from './VerbsScreen'

const apiMock = vi.hoisted(() => ({ getVerbsProgress: vi.fn(), startVerbSession: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function group(overrides: Partial<VerbsProgress['groups'][number]> = {}) {
  return {
    group: 1,
    title: 'All three forms alike',
    total: 9,
    learned: 0,
    unlocked: true,
    families: [],
    ...overrides,
  }
}

const progress: VerbsProgress = {
  learnedPercent: 10,
  groups: [
    group(),
    group({ group: 2, title: 'First and third alike', total: 3, learned: 3, unlocked: true }),
    group({ group: 3, title: 'Second and third alike', total: 29, unlocked: false }),
    group({ group: 4, title: 'All three forms differ', total: 27, unlocked: false }),
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

describe('VerbsScreen', () => {
  it('shows four group tiles, locked ones disabled', async () => {
    const { container } = await render(<VerbsScreen onOpenGroup={() => {}} onStartSession={() => {}} />)
    await flush()

    const tiles = [...container.querySelectorAll('.group-tile')]
    expect(tiles).toHaveLength(4)
    expect(tiles[0].textContent).toContain('All three forms alike')
    expect(tiles[2].querySelector('.btn')?.hasAttribute('disabled')).toBe(true)
    expect(tiles[2].textContent).toContain('Locked')
  })

  it('opens a group when its tile is clicked', async () => {
    const onOpenGroup = vi.fn()
    const { container } = await render(<VerbsScreen onOpenGroup={onOpenGroup} onStartSession={() => {}} />)
    await flush()

    await click(container.querySelectorAll('.group-tile .btn')[0])

    expect(onOpenGroup).toHaveBeenCalledWith(1)
  })

  it('disables errors-only and mixed when unavailable, enables them otherwise', async () => {
    const { container } = await render(<VerbsScreen onOpenGroup={() => {}} onStartSession={() => {}} />)
    await flush()

    expect(container.querySelector('.errors-only-btn')?.hasAttribute('disabled')).toBe(true)
    expect(container.querySelector('.mixed-session-btn')?.hasAttribute('disabled')).toBe(true)
  })

  it('starts a mixed session and reports it', async () => {
    apiMock.getVerbsProgress.mockResolvedValue({ ...progress, mixedAvailable: true })
    const started: SessionStarted = { id: 5, mode: 'mixed', group: null, family: null, title: 'Mixed session', total: 10 }
    apiMock.startVerbSession.mockResolvedValue(started)
    const onStartSession = vi.fn()

    const { container } = await render(<VerbsScreen onOpenGroup={() => {}} onStartSession={onStartSession} />)
    await flush()

    await click(container.querySelector('.mixed-session-btn')!)
    await flush()

    expect(apiMock.startVerbSession).toHaveBeenCalledWith({ mode: 'mixed' })
    expect(onStartSession).toHaveBeenCalledWith(started)
  })

  it('shows a continue banner for an open session', async () => {
    apiMock.getVerbsProgress.mockResolvedValue({
      ...progress,
      activeSession: { id: 7, mode: 'learn', group: 1, family: 'same', answered: 3, total: 16 },
    })
    const onStartSession = vi.fn()

    const { container } = await render(<VerbsScreen onOpenGroup={() => {}} onStartSession={onStartSession} />)
    await flush()

    const banner = container.querySelector('.continue-banner')!
    expect(banner.textContent).toContain('Continue')

    await click(banner.querySelector('.btn')!)

    expect(onStartSession).toHaveBeenCalledWith({ id: 7, mode: 'learn', group: 1, family: 'same', title: '', total: 16 })
  })
})
