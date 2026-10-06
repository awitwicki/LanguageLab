import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GrammarTopic } from '../api/client'
import { click, flush, render } from '../test/render'
import { GrammarScreen } from './GrammarScreen'

const apiMock = vi.hoisted(() => ({ getGrammarTopics: vi.fn(), setGrammarGoal: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

function topic(key: string, section: string, exercises: number, planned = false, level = 'A1'): GrammarTopic {
  return {
    key,
    section,
    level,
    title: `Title ${key}`,
    planned,
    explanation: planned ? [] : ['x'],
    examples: planned ? [] : ['[x]'],
    exercises: Array.from({ length: exercises }, () => ({ sentence: '___', options: ['a', 'b'], answer: 'a', why: 'w' })),
  }
}

describe('GrammarScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMock.setGrammarGoal.mockResolvedValue(null)
  })

  it('groups the topics under their sections, in catalog order', async () => {
    apiMock.getGrammarTopics.mockResolvedValue([topic('be', 'Sentence basics', 5), topic('x', 'Present tenses', 1)])
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={() => {}} onOpenTopic={() => {}} />)
    await flush()

    const sections = container.querySelectorAll('.grammar-section')

    expect(sections).toHaveLength(2)
    expect(sections[0].querySelector('h2')!.textContent).toBe('Sentence basics')
    expect(sections[0].textContent).toContain('Title be')
    expect(sections[0].textContent).toContain('5 exercises')
    expect(sections[1].textContent).toContain('1 exercise')
  })

  it('opens a topic', async () => {
    apiMock.getGrammarTopics.mockResolvedValue([topic('be', 'Sentence basics', 5)])
    const onOpenTopic = vi.fn()
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={() => {}} onOpenTopic={onOpenTopic} />)
    await flush()

    await click(container.querySelector('.grammar-topic')!)

    expect(onOpenTopic).toHaveBeenCalledWith('be')
  })

  /** Review focus 5: a planned row says so and is not a button — nothing to click or tab into. */
  it('shows a planned topic greyed out and not clickable', async () => {
    apiMock.getGrammarTopics.mockResolvedValue([
      topic('be', 'Sentence basics', 5),
      topic('word-order', 'Sentence basics', 0, true),
    ])
    const onOpenTopic = vi.fn()
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={() => {}} onOpenTopic={onOpenTopic} />)
    await flush()

    const planned = container.querySelector('.grammar-topic.is-planned')!

    expect(planned.tagName).not.toBe('BUTTON')
    expect(planned.textContent).toContain('Title word-order')
    expect(planned.textContent).toContain('Coming later')
    expect(planned.textContent).not.toContain('exercise')
    expect(container.querySelectorAll('button.grammar-topic')).toHaveLength(1)

    await click(planned)

    expect(onOpenTopic).not.toHaveBeenCalled()
  })

  it('shows a failed load', async () => {
    apiMock.getGrammarTopics.mockRejectedValue(new Error('GET /api/grammar/topics → 500'))
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={() => {}} onOpenTopic={() => {}} />)
    await flush()

    expect(container.querySelector('.error')!.textContent).toContain('500')
  })

  const levelTopics = () => [
    topic('a1', 'Sentence basics', 5, false, 'A1'),
    topic('a2', 'Present tenses', 5, false, 'A2'),
    topic('b1', 'Past tenses', 0, true, 'B1'),
    topic('b2', 'Future', 0, true, 'B2'),
  ]

  const shownKeys = (container: Element) =>
    [...container.querySelectorAll('.grammar-topic-title')].map((t) => t.textContent)

  const levelButton = (container: Element, level: string) =>
    [...container.querySelectorAll<HTMLButtonElement>('.grammar-goal button')].find((b) => b.textContent === level)!

  it('lists the topics up to the goal and hides the sections left empty', async () => {
    apiMock.getGrammarTopics.mockResolvedValue(levelTopics())
    const { container } = await render(<GrammarScreen goal="A2" onGoalSaved={() => {}} onOpenTopic={() => {}} />)
    await flush()

    expect(shownKeys(container)).toEqual(['Title a1', 'Title a2'])
    expect(container.querySelectorAll('.grammar-section')).toHaveLength(2)
    expect(levelButton(container, 'A2').getAttribute('aria-pressed')).toBe('true')
    expect(levelButton(container, 'B1').getAttribute('aria-pressed')).toBe('false')
  })

  it('picking a level widens the list at once and saves the goal', async () => {
    apiMock.getGrammarTopics.mockResolvedValue(levelTopics())
    const onGoalSaved = vi.fn()
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={onGoalSaved} onOpenTopic={() => {}} />)
    await flush()

    await click(levelButton(container, 'B2'))

    expect(shownKeys(container)).toHaveLength(4)
    expect(apiMock.setGrammarGoal).toHaveBeenCalledWith('B2')

    await flush()

    expect(onGoalSaved).toHaveBeenCalled()
  })

  it('goes back to the saved goal and says so when saving fails', async () => {
    apiMock.getGrammarTopics.mockResolvedValue(levelTopics())
    apiMock.setGrammarGoal.mockRejectedValue(new Error('PUT /api/auth/me/grammar-goal → 500'))
    const onGoalSaved = vi.fn()
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={onGoalSaved} onOpenTopic={() => {}} />)
    await flush()

    await click(levelButton(container, 'B2'))
    await flush()

    expect(shownKeys(container)).toEqual(['Title a1'])
    expect(levelButton(container, 'A1').getAttribute('aria-pressed')).toBe('true')
    expect(container.querySelector('.error')!.textContent).toContain('500')
    expect(onGoalSaved).not.toHaveBeenCalled()
  })

  it('shows a message when nothing is at or below the goal', async () => {
    apiMock.getGrammarTopics.mockResolvedValue([topic('b1', 'Past tenses', 0, true, 'B1')])
    const { container } = await render(<GrammarScreen goal="A1" onGoalSaved={() => {}} onOpenTopic={() => {}} />)
    await flush()

    expect(container.querySelector('.grammar-section')).toBeNull()
    expect(container.querySelector('.grammar-empty')!.textContent).toContain('A1')
  })
})
