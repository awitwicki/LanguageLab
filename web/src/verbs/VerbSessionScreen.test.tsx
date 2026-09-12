import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AnswerFeedback, SessionSummary, SessionStarted, TaskDto } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbSessionScreen } from './VerbSessionScreen'
import { useVerbSession } from './useVerbSession'

vi.mock('./useVerbSession', () => ({ useVerbSession: vi.fn() }))
const apiMock = vi.hoisted(() => ({ startVerbSession: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

const useVerbSessionMock = vi.mocked(useVerbSession)

const gapChoiceTask: TaskDto = {
  id: 1,
  type: 'gapChoice',
  formAsked: 'v2',
  level: 2,
  isReturn: false,
  verb: { v1: 'cut', translation: 'різати', group: 1, family: 'same', suffixes: [] },
  card: null,
  gapChoice: { sentence: 'Yesterday I ___ my finger.', tense: 'past', options: ['wore', 'cut', 'cat', 'cutted'] },
  oddOne: null,
  match: null,
  formPick: null,
  gapType: null,
  tripleType: null,
}

function baseHook(overrides: Partial<ReturnType<typeof useVerbSession>> = {}): ReturnType<typeof useVerbSession> {
  return {
    status: 'task',
    error: null,
    task: gapChoiceTask,
    answered: 0,
    total: 16,
    feedback: null,
    neutralHint: null,
    summary: null,
    answer: vi.fn(),
    next: vi.fn(),
    forgot: vi.fn().mockResolvedValue('cut — back to practice'),
    ...overrides,
  }
}

beforeEach(() => {
  useVerbSessionMock.mockReset()
  apiMock.startVerbSession.mockReset()
})

describe('VerbSessionScreen', () => {
  it('renders the gap-choice task and answers on a number key', async () => {
    const hook = baseHook()
    useVerbSessionMock.mockReturnValue(hook)

    const { container } = await render(<VerbSessionScreen sessionId={1} onBack={() => {}} onRepeatErrors={() => {}} />)
    await flush()

    expect(container.textContent).toContain('Yesterday I ___ my finger.')

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: '2', bubbles: true, cancelable: true }))
    })

    expect(hook.answer).toHaveBeenCalledWith('cut')
  })

  it('shows the progress bar counts', async () => {
    useVerbSessionMock.mockReturnValue(baseHook({ answered: 5, total: 16 }))
    const { container } = await render(<VerbSessionScreen sessionId={1} onBack={() => {}} onRepeatErrors={() => {}} />)
    await flush()

    // answered=5 means the learner is now on card 6 of 16 (1-based, matching TrainingScreen's convention).
    expect(container.textContent).toContain('6')
    expect(container.textContent).toContain('16')
  })

  it('shows feedback with the explanation and a Next button that advances', async () => {
    const feedback: AnswerFeedback = {
      outcome: 'correct',
      taskComplete: true,
      correctAnswer: 'cut',
      triplet: 'cut – cut – cut',
      explanation: "cut doesn't change: cut – cut – cut.",
      errorKind: null,
      willReturn: false,
      matched: null,
    }
    const hook = baseHook({ status: 'feedback', feedback })
    useVerbSessionMock.mockReturnValue(hook)

    const { container } = await render(<VerbSessionScreen sessionId={1} onBack={() => {}} onRepeatErrors={() => {}} />)
    await flush()

    expect(container.textContent).toContain("cut doesn't change")

    await click(container.querySelector('.next-btn')!)
    expect(hook.next).toHaveBeenCalledTimes(1)
  })

  it('shows the summary with mistakes and lets you repeat errors or finish', async () => {
    const summary: SessionSummary = {
      total: 16,
      correct: 10,
      mistakes: [{ v1: 'cut', v2: 'cut', v3: 'cut', translation: 'різати', wrongCount: 2 }],
      learned: [],
    }
    useVerbSessionMock.mockReturnValue(baseHook({ status: 'summary', task: null, summary }))
    const started: SessionStarted = { id: 9, mode: 'errorsOnly', group: null, family: null, title: 'Errors only', total: 2 }
    apiMock.startVerbSession.mockResolvedValue(started)
    const onBack = vi.fn()
    const onRepeatErrors = vi.fn()

    const { container } = await render(<VerbSessionScreen sessionId={1} onBack={onBack} onRepeatErrors={onRepeatErrors} />)
    await flush()

    expect(container.textContent).toContain('10')
    expect(container.textContent).toContain('cut – cut – cut')

    await click(container.querySelector('.repeat-errors-btn')!)
    await flush()

    expect(apiMock.startVerbSession).toHaveBeenCalledWith({ mode: 'errorsOnly', fromSessionId: 1 })
    expect(onRepeatErrors).toHaveBeenCalledWith(started)

    await click(container.querySelector('.done-btn')!)
    expect(onBack).toHaveBeenCalledTimes(1)
  })

  it('flags the current verb as forgotten and shows a notice', async () => {
    const hook = baseHook()
    useVerbSessionMock.mockReturnValue(hook)

    const { container } = await render(<VerbSessionScreen sessionId={1} onBack={() => {}} onRepeatErrors={() => {}} />)
    await flush()

    await click(container.querySelector('.forgot-btn')!)
    await flush()

    expect(hook.forgot).toHaveBeenCalledWith('cut')
    expect(container.textContent).toContain('back to practice')
  })
})
