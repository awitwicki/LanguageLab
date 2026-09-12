import { act, createElement, StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GradeResult, IrregularVerbSession, VerbForm } from '../api/client'
import { useVerbSession } from './useVerbSession'

const apiMock = vi.hoisted(() => ({
  getVerbSession: vi.fn(),
  gradeVerbForm: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

const session: IrregularVerbSession = {
  step: 2,
  title: 'First and third alike',
  review: false,
  cards: [
    { v1: 'come', v2: 'came', v3: 'come', translation: 'приходити', open: 'v1' },
    { v1: 'become', v2: 'became', v3: 'become', translation: 'ставати', open: 'v2' },
  ],
}

const gradeResult = (verb: string): GradeResult => ({
  verb,
  learned: false,
  forms: (['v1', 'v2', 'v3'] as VerbForm[]).map((form) => ({ form, state: 'learning', streak: 1, correct: 1, wrong: 0 })),
})

beforeEach(() => {
  apiMock.getVerbSession.mockReset()
  apiMock.gradeVerbForm.mockReset()
  apiMock.getVerbSession.mockResolvedValue(session)
  apiMock.gradeVerbForm.mockImplementation((verb: string) => Promise.resolve(gradeResult(verb)))
})

async function renderHook<T>(hook: () => T, strict = false) {
  const result = { current: undefined as T }

  function Probe() {
    result.current = hook()
    return null
  }

  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)
  const probe = createElement(Probe)

  await act(async () => {
    root.render(strict ? createElement(StrictMode, null, probe) : probe)
  })

  return result
}

// Lets the mocked load resolve and React commit the resulting state.
async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

describe('useVerbSession', () => {
  it('loads the session and shows the first verb with both closed cards down', async () => {
    const hook = await renderHook(() => useVerbSession(2))
    await settle()

    expect(apiMock.getVerbSession).toHaveBeenCalledWith(2)
    expect(hook.current.status).toBe('active')
    expect(hook.current.session?.title).toBe('First and third alike')
    expect(hook.current.current?.card.v1).toBe('come')
    expect(hook.current.current?.closed).toEqual(['v2', 'v3'])
    expect(hook.current.current?.revealed).toEqual([])
    expect(hook.current.current?.retry).toBe(false)
    expect(hook.current.verbNumber).toBe(1)
    expect(hook.current.total).toBe(2)
    expect(hook.current.toRetryCount).toBe(0)
    expect(hook.current.activeForm).toBeNull()
    expect(hook.current.isDone).toBe(false)
    expect(hook.current.summary).toBeNull()
  })

  it('reveals closed cards left to right and never the open one', async () => {
    const hook = await renderHook(() => useVerbSession(2))
    await settle()

    await act(async () => hook.current.reveal('v1'))
    expect(hook.current.current?.revealed).toEqual([])

    await act(async () => hook.current.reveal())
    expect(hook.current.current?.revealed).toEqual(['v2'])
    expect(hook.current.activeForm).toBe('v2')

    await act(async () => hook.current.reveal())
    expect(hook.current.current?.revealed).toEqual(['v2', 'v3'])
    expect(hook.current.activeForm).toBe('v3')

    await act(async () => hook.current.reveal())
    expect(hook.current.current?.revealed).toEqual(['v2', 'v3'])
  })

  it('grades only revealed forms, posts each grade once, and is done after both', async () => {
    const hook = await renderHook(() => useVerbSession(2))
    await settle()

    await act(async () => hook.current.grade('v2', true))
    expect(apiMock.gradeVerbForm).not.toHaveBeenCalled()

    await act(async () => hook.current.reveal('v3'))
    await act(async () => hook.current.grade('v3', true))
    expect(apiMock.gradeVerbForm).toHaveBeenCalledWith('come', 'v3', true)
    expect(hook.current.current?.grades).toEqual({ v3: true })
    expect(hook.current.activeForm).toBeNull()
    expect(hook.current.isDone).toBe(false)

    await act(async () => hook.current.grade('v3', false))
    expect(apiMock.gradeVerbForm).toHaveBeenCalledTimes(1)

    await act(async () => hook.current.reveal('v2'))
    await act(async () => hook.current.grade('v2', true))
    expect(apiMock.gradeVerbForm).toHaveBeenCalledTimes(2)
    expect(hook.current.isDone).toBe(true)
  })

  it('advances on next only when done and ends in a summary after the last verb', async () => {
    const hook = await renderHook(() => useVerbSession(2))
    await settle()

    await act(async () => hook.current.next())
    expect(hook.current.current?.card.v1).toBe('come')

    for (const v1 of ['come', 'become']) {
      expect(hook.current.current?.card.v1).toBe(v1)

      for (const form of hook.current.current!.closed) {
        await act(async () => hook.current.reveal(form))
        await act(async () => hook.current.grade(form, true))
      }

      await act(async () => hook.current.next())
    }

    expect(hook.current.status).toBe('summary')
    expect(hook.current.current).toBeNull()
    expect(hook.current.summary).toEqual({ formsTotal: 4, formsRightFirstTime: 4, retried: [] })
  })

  it('requeues a missed verb with the form it got right already showing', async () => {
    const hook = await renderHook(() => useVerbSession(2))
    await settle()

    await act(async () => hook.current.reveal('v2'))
    await act(async () => hook.current.grade('v2', false))
    await act(async () => hook.current.reveal('v3'))
    await act(async () => hook.current.grade('v3', true))
    await act(async () => hook.current.next())

    expect(hook.current.current?.card.v1).toBe('become')
    expect(hook.current.toRetryCount).toBe(1)
    expect(hook.current.verbNumber).toBe(1)

    for (const form of hook.current.current!.closed) {
      await act(async () => hook.current.reveal(form))
      await act(async () => hook.current.grade(form, true))
    }

    await act(async () => hook.current.next())

    expect(hook.current.current?.card.v1).toBe('come')
    expect(hook.current.current?.retry).toBe(true)
    expect(hook.current.current?.revealed).toEqual(['v3'])
    expect(hook.current.current?.grades).toEqual({ v3: true })
    expect(hook.current.activeForm).toBeNull()
    expect(hook.current.isDone).toBe(false)
    expect(hook.current.verbNumber).toBe(2)
    expect(hook.current.toRetryCount).toBe(1)

    await act(async () => hook.current.reveal())
    expect(hook.current.current?.revealed).toEqual(['v3', 'v2'])

    await act(async () => hook.current.grade('v2', true))
    expect(apiMock.gradeVerbForm).toHaveBeenLastCalledWith('come', 'v2', true)
    expect(hook.current.isDone).toBe(true)

    await act(async () => hook.current.next())

    expect(hook.current.status).toBe('summary')
    expect(hook.current.summary).toEqual({
      formsTotal: 4,
      formsRightFirstTime: 3,
      retried: [{ card: session.cards[0], missed: ['v2'] }],
    })
  })

  it('reports a failed load', async () => {
    apiMock.getVerbSession.mockRejectedValue(new Error('boom'))

    const hook = await renderHook(() => useVerbSession(2))
    await settle()

    expect(hook.current.status).toBe('error')
    expect(hook.current.error).toContain('boom')
  })

  it('posts a grade once under Strict Mode', async () => {
    const hook = await renderHook(() => useVerbSession(2), true)
    await settle()

    await act(async () => hook.current.reveal('v2'))
    await act(async () => hook.current.grade('v2', true))

    expect(apiMock.gradeVerbForm).toHaveBeenCalledTimes(1)
  })
})
