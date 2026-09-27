import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DrillQuery, SessionVerb, VerbSession } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbDrillScreen } from './VerbDrillScreen'

const apiMock = vi.hoisted(() => ({ getVerbSession: vi.fn(), postVerbAnswers: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function verb(v1: string, overrides: Partial<SessionVerb> = {}): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 4,
    note: null,
    examples: {
      present: `They [${v1}] home early.`,
      past: `They [${v1}-2] home early.`,
      perfect: `They have [${v1}-3] home early.`,
    },
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
    fresh: true,
    ...overrides,
  }
}

const session: VerbSession = {
  mode: 'batch',
  verbs: [verb('go'), verb('see')],
  queue: null,
  scope: { passed: 2, total: 27 },
}

function screen(onStartDrill: (query: DrillQuery, title: string) => void = () => {}) {
  return (
    <VerbDrillScreen
      query={{ mode: 'batch', group: 4 }}
      title="All three forms differ"
      onBack={() => {}}
      onStartDrill={onStartDrill}
    />
  )
}

/// Opens the screen and gets past the start screen into the round under test.
async function open(into: 'intro' | 'drill') {
  const { container } = await render(screen())
  await flush()

  await click(container.querySelector(into === 'intro' ? '.start-intro' : '.start-skip')!)
  await flush()

  return container
}

describe('VerbDrillScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    apiMock.getVerbSession.mockResolvedValue(session)
    apiMock.postVerbAnswers.mockResolvedValue({ results: [] })
  })

  it('opens on the start screen', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.start-summary')!.textContent).toBe('2 words × 3 rounds = 6 cards')
    expect(container.querySelector('.drill-prompt')).toBeNull()
  })

  it('shows an introduction card with nothing to judge', async () => {
    const container = await open('intro')

    expect(container.querySelector('.drill-prompt')!.textContent).toBe('go-2')
    expect(container.querySelector('.drill-answer')!.className).toContain('is-blurred')
    expect(container.querySelector('.drill-next')).not.toBeNull()
    expect(container.querySelector('.btn-known')).toBeNull()
    expect(container.querySelector('.btn-unknown')).toBeNull()
    expect(container.textContent).toContain('1 of 6')
  })

  it('moves on from an introduction card without uncovering it', async () => {
    const container = await open('intro')

    await click(container.querySelector('.drill-next')!)
    await flush()

    expect(container.querySelector('.drill-prompt')!.textContent).toBe('see-2')
    expect(container.querySelector('.drill-answer')!.className).toContain('is-blurred')
    expect(container.textContent).toContain('2 of 6')
    expect(apiMock.postVerbAnswers).not.toHaveBeenCalled()
  })

  it('leaves the introduction for the verdict buttons', async () => {
    const container = await open('intro')

    await click(container.querySelector('.drill-skip-intro')!)
    await flush()

    expect(container.querySelector('.btn-known')).not.toBeNull()
    expect(container.querySelector('.drill-next')).toBeNull()
  })

  it('takes "I know" straight to the next card', async () => {
    const container = await open('drill')

    expect(container.querySelector('.drill-prompt')!.textContent).toBe('go-2')

    await click(container.querySelector('.btn-known')!)
    await flush()

    // No reveal, no example, no second click: the next card is already up.
    expect(container.querySelector('.drill-answer')!.className).toContain('is-blurred')
    expect(container.querySelector('.drill-example')).toBeNull()
    expect(container.querySelector('.drill-prompt')!.textContent).toBe('see-2')
    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([
      expect.objectContaining({ verb: 'go', known: true }),
    ])
  })

  it('shows the answer and the example on "I don\'t know"', async () => {
    const container = await open('drill')

    await click(container.querySelector('.btn-unknown')!)
    await flush()

    expect(container.querySelector('.drill-answer')!.className).not.toContain('is-blurred')
    expect(container.textContent).toContain('переклад go')
    // The catalog brackets the verb form; the example emphasises it rather than dropping the
    // brackets and leaving the sentence flat.
    expect(container.querySelector('.drill-example strong')!.textContent).toBe('go-2')
    expect(container.querySelector('.drill-example')!.textContent).not.toContain('[')
    expect(container.querySelector('.drill-next')).not.toBeNull()

    await click(container.querySelector('.drill-next')!)
    await flush()

    expect(container.querySelector('.drill-prompt')!.textContent).toBe('see-2')
  })

  it('uncovers the answer when the blurred answer itself is clicked', async () => {
    const container = await open('drill')

    await click(container.querySelector('.drill-answer')!)

    expect(container.querySelector('.drill-answer')!.className).not.toContain('is-blurred')
    // A peek judges nothing — the verdict buttons stay, and nothing is posted.
    expect(apiMock.postVerbAnswers).not.toHaveBeenCalled()
    expect(container.querySelector('.btn-known')).not.toBeNull()
    expect(container.querySelector('.drill-next')).toBeNull()
  })

  it('keeps the blurred answer away from screen readers until it is shown', async () => {
    const container = await open('drill')

    expect(container.querySelector('.drill-triplet')!.getAttribute('aria-hidden')).toBe('true')

    await click(container.querySelector('.drill-answer')!)

    expect(container.querySelector('.drill-triplet')!.getAttribute('aria-hidden')).toBeNull()
  })

  it('answers from the keyboard and advances with space', async () => {
    const container = await open('drill')

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }))
    await flush()

    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([
      expect.objectContaining({ verb: 'go', known: false }),
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }))
    await flush()

    expect(container.querySelector('.drill-prompt')!.textContent).toBe('see-2')
  })

  /// A repeated keydown (a held key) must not double-answer the card it lands on — the
  /// window-level shortcut has no card identity of its own to guard with, unlike answer().
  it('ignores a held key so it cannot answer the same card twice', async () => {
    await open('drill')

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', repeat: true }))
    await flush()

    expect(apiMock.postVerbAnswers).not.toHaveBeenCalled()
  })

  /// A keyboard user tabbed to a real control (a stepper, Skip to training, Back) must have
  /// Enter reach that control, not the window-level shortcut for the phase's main action.
  it('leaves Enter on a focused button to the button itself', async () => {
    const { container } = await render(screen())
    await flush()

    const skip = container.querySelector<HTMLButtonElement>('.start-skip')!
    skip.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flush()

    // Enter landing on "Skip to training" must not also start the introduction.
    expect(container.querySelector('.start-summary')).not.toBeNull()
    expect(container.querySelector('.drill-prompt')).toBeNull()
  })

  it('shows the scope progress', async () => {
    const container = await open('drill')

    expect(container.textContent).toContain('2 of 27')
  })

  it('reports the words it finished and offers the next ones', async () => {
    apiMock.getVerbSession.mockResolvedValue({ ...session, verbs: [verb('go')] })
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector('.start-skip')!)
    await flush()

    for (let i = 0; i < 4; i++) {
      await click(container.querySelector('.btn-known')!)
      await flush()
    }

    expect(container.textContent).toContain('has passed')
    expect(container.querySelector('.drill-next-words')).not.toBeNull()
  })

  it('reports a finished stage instead of a card', async () => {
    apiMock.getVerbSession.mockResolvedValue(null)

    const { container } = await render(screen())
    await flush()

    expect(container.textContent).toContain('Every verb of this stage has passed')
    expect(container.querySelector('.drill-prompt')).toBeNull()
  })

  it('offers free training on the stage it just finished', async () => {
    apiMock.getVerbSession.mockResolvedValue(null)
    const onStartDrill = vi.fn()

    const { container } = await render(screen(onStartDrill))
    await flush()

    await click(container.querySelector('.drill-free')!)

    expect(onStartDrill).toHaveBeenCalledWith({ mode: 'free', group: 4, scope: 'stage' }, 'All three forms differ')
  })

  it('surfaces a failed fetch', async () => {
    apiMock.getVerbSession.mockRejectedValue(new Error('no network'))

    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.error')!.textContent).toContain('no network')
  })
})
