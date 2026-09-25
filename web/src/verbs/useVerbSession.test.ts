import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AnswerFeedback, NextTask, SessionSummary, TaskDto } from '../api/client'
import { useVerbSession } from './useVerbSession'

const apiMock = vi.hoisted(() => ({
  nextVerbTask: vi.fn(),
  answerVerbTask: vi.fn(),
  finishVerbSession: vi.fn(),
  forgotVerb: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

const cardTask: TaskDto = {
  id: 1,
  type: 'card',
  formAsked: 'recognition',
  level: 1,
  isReturn: false,
  verb: { v1: 'cut', translation: 'різати', group: 1, family: 'same', suffixes: [] },
  card: { v2: ['cut'], v3: ['cut'], examples: [], note: null },
  gapChoice: null,
  oddOne: null,
  match: null,
  formPick: null,
  gapType: null,
  tripleType: null,
}

const matchTask: TaskDto = {
  ...cardTask,
  id: 2,
  type: 'match',
  card: null,
  match: {
    form: 'v2',
    lefts: ['buy', 'bring'],
    rights: ['bought', 'brought'],
    matched: [],
  },
}

const feedbackCorrect: AnswerFeedback = {
  outcome: 'correct',
  taskComplete: true,
  correctAnswer: 'cut – cut – cut',
  triplet: 'cut – cut – cut',
  explanation: "cut doesn't change: cut – cut – cut.",
  errorKind: null,
  willReturn: false,
  matched: null,
}

const feedbackWrong: AnswerFeedback = {
  outcome: 'wrong',
  taskComplete: true,
  correctAnswer: 'cut',
  triplet: 'cut – cut – cut',
  explanation: "cut doesn't change: cut – cut – cut.",
  errorKind: 'other',
  willReturn: true,
  matched: null,
}

const feedbackNeutral: AnswerFeedback = {
  outcome: 'neutral',
  taskComplete: false,
  correctAnswer: 'bought',
  triplet: 'buy – bought – bought',
  explanation: 'V2 and V3 are the same: buy – bought – bought.',
  errorKind: 'spelling',
  willReturn: false,
  matched: null,
}

const feedbackMatchPartial: AnswerFeedback = {
  outcome: 'correct',
  taskComplete: false,
  correctAnswer: 'bought',
  triplet: 'buy – bought – bought',
  explanation: 'x',
  errorKind: null,
  willReturn: false,
  matched: [{ left: 'buy', right: 'bought' }],
}

async function renderHook<T>(hook: () => T) {
  const result = { current: undefined as T }

  function Probe() {
    result.current = hook()
    return null
  }

  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  await act(async () => {
    root.render(createElement(Probe))
  })

  return result
}

async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(() => {
  apiMock.nextVerbTask.mockReset()
  apiMock.answerVerbTask.mockReset()
  apiMock.finishVerbSession.mockReset()
  apiMock.forgotVerb.mockReset()
})

describe('useVerbSession', () => {
  it('loads the first task', async () => {
    apiMock.nextVerbTask.mockResolvedValue({ task: cardTask, answered: 0, total: 16 } satisfies NextTask)

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    expect(apiMock.nextVerbTask).toHaveBeenCalledWith(1)
    expect(hook.current.status).toBe('task')
    expect(hook.current.task?.id).toBe(1)
    expect(hook.current.answered).toBe(0)
    expect(hook.current.total).toBe(16)
  })

  it('shows feedback and waits after a wrong answer', async () => {
    apiMock.nextVerbTask.mockResolvedValue({ task: cardTask, answered: 0, total: 16 } satisfies NextTask)
    apiMock.answerVerbTask.mockResolvedValue(feedbackWrong)

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    await act(async () => {
      await hook.current.answer('cat')
    })

    expect(apiMock.answerVerbTask).toHaveBeenCalledWith(1, 1, 'cat', undefined)
    expect(hook.current.status).toBe('feedback')
    expect(hook.current.feedback?.explanation).toContain("doesn't change")
  })

  it('skips feedback and loads the next task straight away on a correct answer', async () => {
    apiMock.nextVerbTask.mockResolvedValue({ task: cardTask, answered: 0, total: 16 } satisfies NextTask)
    apiMock.answerVerbTask.mockResolvedValue(feedbackCorrect)

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    await act(async () => {
      await hook.current.answer('seen')
    })
    await settle()

    expect(apiMock.answerVerbTask).toHaveBeenCalledWith(1, 1, 'seen', undefined)
    expect(apiMock.nextVerbTask).toHaveBeenCalledTimes(2)
    expect(hook.current.status).toBe('task')
    expect(hook.current.feedback).toBeNull()
  })

  it('stays on the task with a neutral hint on a near-miss spelling', async () => {
    apiMock.nextVerbTask.mockResolvedValue({ task: cardTask, answered: 0, total: 16 } satisfies NextTask)
    apiMock.answerVerbTask.mockResolvedValue(feedbackNeutral)

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    await act(async () => {
      await hook.current.answer('boght')
    })

    expect(hook.current.status).toBe('task')
    expect(hook.current.neutralHint).toContain('same')
  })

  it('updates matched pairs and stays on the task while a match is incomplete', async () => {
    apiMock.nextVerbTask.mockResolvedValue({ task: matchTask, answered: 0, total: 16 } satisfies NextTask)
    apiMock.answerVerbTask.mockResolvedValue(feedbackMatchPartial)

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    await act(async () => {
      await hook.current.answer('buy=bought')
    })

    expect(hook.current.status).toBe('task')
    expect(hook.current.task?.match?.matched).toEqual([{ left: 'buy', right: 'bought' }])
  })

  it('finishes and shows the summary once the queue is empty', async () => {
    apiMock.nextVerbTask
      .mockResolvedValueOnce({ task: cardTask, answered: 0, total: 1 } satisfies NextTask)
      .mockResolvedValueOnce({ task: null, answered: 1, total: 1 } satisfies NextTask)
    apiMock.answerVerbTask.mockResolvedValue(feedbackCorrect)
    const summary: SessionSummary = { total: 1, correct: 1, mistakes: [], learned: [] }
    apiMock.finishVerbSession.mockResolvedValue(summary)

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    await act(async () => {
      await hook.current.answer('seen')
    })
    await settle()

    expect(apiMock.finishVerbSession).toHaveBeenCalledWith(1)
    expect(hook.current.status).toBe('summary')
    expect(hook.current.summary?.total).toBe(1)
  })

  it('forgot posts for the given verb and returns a notice', async () => {
    apiMock.nextVerbTask.mockResolvedValue({ task: cardTask, answered: 0, total: 16 } satisfies NextTask)
    apiMock.forgotVerb.mockResolvedValue({ v1: 'cut', state: 'forgotten' })

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    let notice: string | undefined
    await act(async () => {
      notice = await hook.current.forgot('cut')
    })

    expect(apiMock.forgotVerb).toHaveBeenCalledWith('cut')
    expect(notice).toContain('cut')
  })

  it('reports a failed load', async () => {
    apiMock.nextVerbTask.mockRejectedValue(new Error('boom'))

    const hook = await renderHook(() => useVerbSession(1))
    await settle()

    expect(hook.current.status).toBe('error')
    expect(hook.current.error).toContain('boom')
  })
})
