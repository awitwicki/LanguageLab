import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DrillQuery, PromptForm, SessionVerb, VerbExercise, VerbSession } from '../api/client'
import { click, flush, render } from '../test/render'
import { readSavedSession } from './savedSession'
import { VerbDrillScreen } from './VerbDrillScreen'

const apiMock = vi.hoisted(() => ({ getVerbSession: vi.fn(), postVerbAnswers: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function verb(v1: string, overrides: Partial<SessionVerb> = {}): SessionVerb {
  const exercise = (form: PromptForm, answer: string): VerbExercise => ({
    form,
    before: 'Yesterday they ',
    after: ' home.',
    options: [answer, `${v1}-x`, `${v1}-y`, `${v1}ed`],
    answer,
  })

  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 4,
    note: null,
    exercises: [exercise('v1', v1), exercise('v2', `${v1}-2`), exercise('v3', `${v1}-3`)],
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
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
      userId={7}
      onBack={() => {}}
      onStartDrill={onStartDrill}
    />
  )
}

/// Opens the screen and gets past the start screen into the round under test.
async function open() {
  const { container } = await render(screen())
  await flush()

  await click(container.querySelector('.start-train')!)
  await flush()

  return container
}

const option = (container: Element, text: string) =>
  container.querySelector<HTMLButtonElement>(`.drill-option[data-option="${text}"]`)!

function key(k: string) {
  return act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: k }))
  })
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

    expect(container.querySelector('.start-words')).not.toBeNull()
    expect(container.querySelector('.drill-sentence')).toBeNull()
  })

  it('shows the sentence with a blank, the translation and the options', async () => {
    const container = await open()

    expect(container.querySelector('.drill-sentence')!.textContent).toBe('Yesterday they ___ home.')
    expect(container.textContent).toContain('переклад go')
    expect(container.querySelectorAll('.drill-option')).toHaveLength(4)
    expect(container.textContent).not.toContain('go – go-2 – go-3')
  })

  it('takes a right pick straight to the next card', async () => {
    const container = await open()

    await click(option(container, 'go-2'))

    expect(container.textContent).toContain('переклад see')
    expect(apiMock.postVerbAnswers).toHaveBeenCalledWith([
      expect.objectContaining({ verb: 'go', promptForm: 'v2', chosen: 'go-2' }),
    ])
  })

  it('marks a wrong pick, shows the right one and the forms, and waits for Next', async () => {
    const container = await open()

    await click(option(container, 'goed'))

    expect(option(container, 'goed').className).toContain('btn-unknown')
    expect(option(container, 'go-2').className).toContain('btn-known')
    expect(container.querySelector('.drill-blank')!.textContent).toBe('go-2')
    expect(container.textContent).toContain('go – go-2 – go-3')

    await click(container.querySelector('.drill-next')!)

    expect(container.textContent).toContain('переклад see')
  })

  /// A real click focuses the button that was clicked; unlike the old drill, the picked
  /// option stays on screen (for its colour) instead of unmounting, so nothing else moves
  /// focus off it. Without this, Space on a focused option hits its own native activation —
  /// re-picking the same option — instead of the window shortcut for Next.
  it('moves focus to Next after a wrong pick, so a focused option does not swallow Space', async () => {
    const container = await open()
    const wrongOption = option(container, 'goed')
    wrongOption.focus() // the test's click() helper does not focus, unlike a real click

    await click(wrongOption)

    expect(document.activeElement).toBe(container.querySelector('.drill-next'))
  })

  it('picks with number keys and moves on with space', async () => {
    const container = await open()
    const wrongIndex = [...container.querySelectorAll('.drill-option')].findIndex(
      (b) => b.getAttribute('data-option') !== 'go-2',
    )

    await key(String(wrongIndex + 1))
    expect(container.querySelector('.drill-next')).not.toBeNull()

    await key(' ')
    expect(container.textContent).toContain('переклад see')
  })

  /// Review focus 1.
  it('records one answer for a double pick on the same card', async () => {
    const container = await open()

    await click(option(container, 'goed'))
    await click(option(container, 'go-x'))
    await key('1')
    await flush()

    const posted = apiMock.postVerbAnswers.mock.calls.flatMap((call) => call[0])
    expect(posted).toHaveLength(1)
    expect(posted[0].chosen).toBe('goed')
  })

  /// A repeated keydown (a held key) must not double-answer the card it lands on — the
  /// window-level shortcut has no card identity of its own to guard with, unlike answer().
  it('ignores a held key so it cannot answer the same card twice', async () => {
    await open()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: '1', repeat: true }))
    await flush()

    expect(apiMock.postVerbAnswers).not.toHaveBeenCalled()
  })

  /// A keyboard user tabbed to a real control (a stepper, Back) must have Enter reach that
  /// control, not the window-level shortcut for the phase's main action.
  it('leaves Enter on a focused button to the button itself', async () => {
    const { container } = await render(screen())
    await flush()

    const stepper = container.querySelector<HTMLButtonElement>('.start-words-more')!
    stepper.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flush()

    // Enter landing on the stepper must not also start training.
    expect(container.querySelector('.start-words')).not.toBeNull()
    expect(container.querySelector('.drill-sentence')).toBeNull()
  })

  it('shows the scope progress', async () => {
    const container = await open()

    expect(container.textContent).toContain('2 of 27')
  })

  it('reports the words it finished and offers the next ones', async () => {
    apiMock.getVerbSession.mockResolvedValue({ ...session, verbs: [verb('go')] })
    const container = await open()

    // The fixture always puts the right form first, whichever form the card is showing.
    for (let i = 0; i < 4; i++) {
      await click(container.querySelector('.drill-option')!)
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
    expect(container.querySelector('.drill-sentence')).toBeNull()
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

  it('keeps the round for a reload, and forgets it on Back', async () => {
    const container = await open()

    expect(readSavedSession(7)).not.toBeNull()

    await click(container.querySelector('.drill-head .btn-quiet')!)

    expect(readSavedSession(7)).toBeNull()
  })
})
