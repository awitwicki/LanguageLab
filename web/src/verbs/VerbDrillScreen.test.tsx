import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DrillCard, DrillQuery, VerbAnswerResult } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbDrillScreen } from './VerbDrillScreen'

const apiMock = vi.hoisted(() => ({ nextVerbCard: vi.fn(), answerVerbCard: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

const card: DrillCard = {
  verb: { v1: 'go', v2: 'went', v3: 'gone', translation: 'йти', group: 4, note: null },
  promptForm: 'v2',
  example: { tense: 'past', text: 'They [went] home early.' },
  scope: { passed: 2, total: 27 },
}

const result: VerbAnswerResult = { verb: 'go', mastery: 0.8, streak: 2, passed: false }

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

describe('VerbDrillScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMock.nextVerbCard.mockResolvedValue(card)
    apiMock.answerVerbCard.mockResolvedValue(result)
  })

  it('shows the prompted form and hides the answer', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.drill-prompt')!.textContent).toBe('went')
    expect(container.querySelector('.drill-answer')!.className).toContain('is-blurred')
    // The forms are in the DOM from the start — there is nothing else to blur.
    expect(container.querySelector('.drill-answer')!.textContent).toContain('go – went – gone')
  })

  it('reveals the answer on a verdict and offers the next card', async () => {
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector('.btn-known')!)
    await flush()

    expect(container.querySelector('.drill-answer')!.className).not.toContain('is-blurred')
    expect(container.textContent).toContain('йти')
    expect(container.textContent).toContain('They went home early.')
    // The catalog brackets the verb form; the example emphasises it rather than dropping
    // the brackets and leaving the sentence flat.
    expect(container.querySelector('.drill-example strong')!.textContent).toBe('went')
    expect(container.querySelector('.drill-example')!.textContent).not.toContain('[')
    expect(container.querySelector('.drill-next')).not.toBeNull()
    expect(apiMock.answerVerbCard).toHaveBeenCalledWith(expect.objectContaining({ known: true }))
  })

  it('records a miss through the other button', async () => {
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector('.btn-unknown')!)
    await flush()

    expect(apiMock.answerVerbCard).toHaveBeenCalledWith(expect.objectContaining({ known: false }))
  })

  it('answers from the keyboard and advances with space', async () => {
    const { container } = await render(screen())
    await flush()

    await flush()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }))
    await flush()

    expect(apiMock.answerVerbCard).toHaveBeenCalledWith(expect.objectContaining({ known: true }))

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }))
    await flush()

    expect(apiMock.nextVerbCard).toHaveBeenCalledTimes(2)
    expect(container.querySelector('.drill-answer')!.className).toContain('is-blurred')
  })

  it('shows the scope progress', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.textContent).toContain('2 of 27')
  })

  it('uncovers the answer when the blurred answer itself is clicked', async () => {
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector('.drill-answer')!)

    expect(container.querySelector('.drill-answer')!.className).not.toContain('is-blurred')
    // A peek judges nothing — the verdict buttons stay, and nothing is posted.
    expect(apiMock.answerVerbCard).not.toHaveBeenCalled()
    expect(container.querySelector('.btn-known')).not.toBeNull()
    expect(container.querySelector('.drill-next')).toBeNull()
  })

  it('still takes the verdict after the answer was peeked at', async () => {
    const { container } = await render(screen())
    await flush()

    await click(container.querySelector('.drill-answer')!)
    await click(container.querySelector('.btn-unknown')!)
    await flush()

    expect(apiMock.answerVerbCard).toHaveBeenCalledWith(expect.objectContaining({ known: false }))
    expect(container.querySelector('.drill-next')).not.toBeNull()
  })

  it('keeps the blurred answer away from screen readers until it is shown', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.drill-triplet')!.getAttribute('aria-hidden')).toBe('true')

    await click(container.querySelector('.drill-answer')!)

    expect(container.querySelector('.drill-triplet')!.getAttribute('aria-hidden')).toBeNull()
  })

  it('reports a finished stage instead of a card', async () => {
    apiMock.nextVerbCard.mockResolvedValue(null)

    const { container } = await render(screen())
    await flush()

    expect(container.textContent).toContain('Every verb of this stage has passed')
    expect(container.querySelector('.drill-prompt')).toBeNull()
  })

  it('offers free training on the stage it just finished', async () => {
    apiMock.nextVerbCard.mockResolvedValue(null)
    const onStartDrill = vi.fn()

    const { container } = await render(screen(onStartDrill))
    await flush()

    await click(container.querySelector('.drill-free')!)

    expect(onStartDrill).toHaveBeenCalledWith(
      { mode: 'free', group: 4, scope: 'stage' },
      'All three forms differ',
    )
  })
})
