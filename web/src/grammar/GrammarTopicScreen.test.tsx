import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GrammarTopic } from '../api/client'
import { click, flush, render } from '../test/render'
import { GrammarTopicScreen, RIGHT_PAUSE_MS } from './GrammarTopicScreen'

const apiMock = vi.hoisted(() => ({ getGrammarTopics: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

const be: GrammarTopic = {
  key: 'be',
  section: 'Sentence basics',
  level: 'A1',
  title: 'be: am, is, are',
  planned: false,
  explanation: ['I → [am]. He, she, it → [is].'],
  examples: ['She [is] at work.'],
  exercises: [
    { sentence: 'My parents ___ at home.', options: ['am', 'is', 'are'], answer: 'are', why: 'My parents = they → are.' },
    { sentence: 'I ___ from Ukraine.', options: ['am', 'is', 'are'], answer: 'am', why: 'I → am.' },
  ],
}

function key(k: string, repeat = false) {
  return act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key: k, repeat }))
  })
}

const option = (container: Element, text: string) =>
  container.querySelector<HTMLButtonElement>(`.grammar-option[data-option="${text}"]`)!

/** The card on screen, read back from the sentence: before + after around the blank. */
function sentence(container: Element) {
  return container.querySelector('.grammar-sentence')!.textContent!
}

async function openTopic(onBack = () => {}) {
  const { container } = await render(<GrammarTopicScreen topicKey="be" onBack={onBack} />)
  await flush()

  return container
}

async function startExercises(container: Element) {
  await click(container.querySelector('.grammar-start')!)
}

/** Picks the right option for whatever card is showing. */
async function pickRight(container: Element) {
  const right = sentence(container).startsWith('My parents') ? 'are' : 'am'
  await click(option(container, right))
}

async function pickWrong(container: Element) {
  await click(option(container, 'is'))
}

describe('GrammarTopicScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    apiMock.getGrammarTopics.mockResolvedValue([be])
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('opens on the explanation with the bracketed forms in bold', async () => {
    const container = await openTopic()

    expect(container.querySelector('h1')!.textContent).toBe('be: am, is, are')
    expect([...container.querySelectorAll('.grammar-explanation strong')].map((s) => s.textContent)).toEqual(['am', 'is'])
    expect(container.querySelector('.grammar-examples strong')!.textContent).toBe('is')
    expect(container.querySelector('.grammar-option')).toBeNull()
  })

  it('Enter starts the exercises', async () => {
    const container = await openTopic()

    await key('Enter')

    expect(container.querySelectorAll('.grammar-option')).toHaveLength(3)
    expect(container.querySelector('.grammar-counter')!.textContent).toBe('1 / 2')
  })

  it('keeps the explanation on screen beside the exercises', async () => {
    const container = await openTopic()
    await startExercises(container)

    expect(container.querySelector('h1')!.textContent).toBe('be: am, is, are')
    expect(container.querySelectorAll('.grammar-explanation p')).toHaveLength(be.explanation.length)
    expect(container.querySelector('.grammar-examples')).not.toBeNull()
    expect(container.querySelector('.grammar-option')).not.toBeNull()
  })

  it('a right pick fills the blank and moves on by itself', async () => {
    const container = await openTopic()
    await startExercises(container)
    const first = sentence(container)

    await pickRight(container)

    expect(container.querySelector('.grammar-blank.is-filled')).not.toBeNull()
    expect(container.querySelector('.grammar-why')).toBeNull()

    await act(async () => vi.advanceTimersByTime(RIGHT_PAUSE_MS))

    expect(sentence(container)).not.toBe(first)
    expect(container.querySelector('.grammar-counter')!.textContent).toBe('2 / 2')
  })

  it('a wrong pick shows the right form and why, and waits for Next', async () => {
    const container = await openTopic()
    await startExercises(container)

    await pickWrong(container)

    expect(option(container, 'is').className).toContain('btn-unknown')
    expect(container.querySelector('.grammar-blank.is-filled')!.textContent).not.toBe('is')
    expect(container.querySelector('.grammar-why')).not.toBeNull()

    await act(async () => vi.advanceTimersByTime(RIGHT_PAUSE_MS * 3))
    expect(container.querySelector('.grammar-counter')!.textContent).toBe('1 / 2')

    await key(' ')
    expect(container.querySelector('.grammar-counter')!.textContent).toBe('2 / 2')
  })

  /**
   * Review focus 1: a clicked option keeps focus in a browser, and the key handler ignores keys
   * while a button is focused. Both topics reuse the same options, so the next card's buttons
   * must be new elements — the clicked one leaves the document and focus with it.
   */
  it('a clicked option is replaced on the next card, so number keys keep working', async () => {
    const container = await openTopic()
    await startExercises(container)
    const right = sentence(container).startsWith('My parents') ? 'are' : 'am'
    const clicked = option(container, right)
    clicked.focus()
    await click(clicked)
    await act(async () => vi.advanceTimersByTime(RIGHT_PAUSE_MS))

    expect(clicked.isConnected).toBe(false)

    await key('1')
    expect(container.querySelector('.grammar-option[aria-pressed="true"]')).not.toBeNull()
  })

  /** Review focus 2: a key during the pause after a right pick changes nothing. */
  it('ignores keys during the pause after a right pick', async () => {
    const container = await openTopic()
    await startExercises(container)
    await pickRight(container)

    await key('2')
    await key(' ')

    expect(container.querySelector('.grammar-counter')!.textContent).toBe('1 / 2')
  })

  it('ignores a held key', async () => {
    const container = await openTopic()
    await startExercises(container)

    await key('1', true)

    expect(container.querySelector('.grammar-option[aria-pressed="true"]')).toBeNull()
  })

  it('ends on the result with the missed sentence filled in, and Try again restarts', async () => {
    const container = await openTopic()
    await startExercises(container)

    const firstWasParents = sentence(container).startsWith('My parents')
    await pickWrong(container)
    await key('Enter')
    await pickRight(container)
    await act(async () => vi.advanceTimersByTime(RIGHT_PAUSE_MS))

    expect(container.querySelector('.grammar-score')!.textContent).toBe('1 of 2 right')
    expect(container.querySelector('.grammar-missed li')!.textContent).toBe(
      firstWasParents ? 'My parents are at home.' : 'I am from Ukraine.',
    )

    await click(container.querySelector('.grammar-again')!)

    expect(container.querySelector('.grammar-counter')!.textContent).toBe('1 / 2')
  })

  it('Back to topics leaves', async () => {
    const onBack = vi.fn()
    const container = await openTopic(onBack)
    await startExercises(container)
    await pickRight(container)
    await act(async () => vi.advanceTimersByTime(RIGHT_PAUSE_MS))
    await pickRight(container)
    await act(async () => vi.advanceTimersByTime(RIGHT_PAUSE_MS))

    await click(container.querySelector('.grammar-leave')!)

    expect(onBack).toHaveBeenCalled()
  })

  /** Review focus 4: a stale route whose topic is gone goes back to the list. */
  it('goes back when the topic is not in the catalog', async () => {
    apiMock.getGrammarTopics.mockResolvedValue([])
    const onBack = vi.fn()
    await openTopic(onBack)

    expect(onBack).toHaveBeenCalled()
  })

  /** Review focus 3: a planned topic has nothing to show, so it is treated like a missing one. */
  it('goes back when the topic is only planned', async () => {
    apiMock.getGrammarTopics.mockResolvedValue([{ ...be, planned: true, explanation: [], examples: [], exercises: [] }])
    const onBack = vi.fn()
    await openTopic(onBack)

    expect(onBack).toHaveBeenCalled()
  })

  it('shows a failed load', async () => {
    apiMock.getGrammarTopics.mockRejectedValue(new Error('GET /api/grammar/topics → 500'))
    const container = await openTopic()

    expect(container.querySelector('.error')!.textContent).toContain('500')
  })
})
