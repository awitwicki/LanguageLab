import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { PronunciationAttemptResult, PronunciationFamily, PronunciationNextWord, PronunciationWordDto } from '../api/client'
import { usePronunciationFamily } from './usePronunciationFamily'

const apiMock = vi.hoisted(() => ({
  getPronunciationFamily: vi.fn(),
  nextPronunciationWord: vi.fn(),
  submitPronunciationAttempt: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

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

const word: PronunciationWordDto = {
  word: 'ship',
  ipa: '/ʃɪp/',
  audioUs: '/pronunciation-audio/ship-us.ogg',
  audioUk: '/pronunciation-audio/ship-uk.ogg',
  state: 'new',
  streak: 0,
}

const family: PronunciationFamily = {
  key: 'ih-vs-iy',
  title: 'Ɪ vs Iː',
  targetSounds: ['ɪ', 'iː'],
  words: [word],
}

beforeEach(() => {
  localStorage.clear()
  apiMock.getPronunciationFamily.mockReset().mockResolvedValue(family)
  apiMock.nextPronunciationWord.mockReset().mockResolvedValue({ word } satisfies PronunciationNextWord)
  apiMock.submitPronunciationAttempt.mockReset()
})

describe('usePronunciationFamily', () => {
  it('loads the family title and the first word', async () => {
    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()

    expect(apiMock.getPronunciationFamily).toHaveBeenCalledWith('ih-vs-iy')
    expect(apiMock.nextPronunciationWord).toHaveBeenCalledWith('ih-vs-iy', false)
    expect(hook.current.status).toBe('ready')
    expect(hook.current.word?.word).toBe('ship')
  })

  it('shows family-complete when next returns no word', async () => {
    apiMock.nextPronunciationWord.mockResolvedValue({ word: null } satisfies PronunciationNextWord)

    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()

    expect(hook.current.status).toBe('family-complete')
  })

  it('submits a transcript and shows feedback', async () => {
    apiMock.submitPronunciationAttempt.mockResolvedValue({
      outcome: 'correct',
      score: 90,
      state: 'learning',
      streak: 1,
      familyDone: false,
    } satisfies PronunciationAttemptResult)

    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()

    await act(async () => {
      await hook.current.submitTranscript('ship')
    })

    expect(apiMock.submitPronunciationAttempt).toHaveBeenCalledWith('ship', 'us', 'ship')
    expect(hook.current.status).toBe('feedback')
    expect(hook.current.feedback?.outcome).toBe('correct')
  })

  it('surfaces a failed attempt instead of an unhandled rejection', async () => {
    // request() throws on any non-2xx, and the screen calls submitTranscript as a
    // floating promise — so without a catch here the attempt disappears and the
    // screen keeps showing the word with no sign that anything went wrong.
    apiMock.submitPronunciationAttempt.mockRejectedValue(new Error('500 Internal Server Error'))

    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()
    await act(async () => {
      await hook.current.submitTranscript('ship')
    })

    expect(hook.current.attemptError).toMatch(/try again/i)
    expect(hook.current.feedback).toBeNull()
    // The word is still there and still practisable: this is not the fatal load error.
    expect(hook.current.status).toBe('ready')
    expect(hook.current.error).toBeNull()
    expect(hook.current.word?.word).toBe('ship')
  })

  it('clears a previous attempt error once an attempt scores', async () => {
    apiMock.submitPronunciationAttempt.mockRejectedValueOnce(new Error('500')).mockResolvedValue({
      outcome: 'correct',
      score: 90,
      state: 'learning',
      streak: 1,
      familyDone: false,
    } satisfies PronunciationAttemptResult)

    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()
    await act(async () => {
      await hook.current.submitTranscript('ship')
    })
    expect(hook.current.attemptError).not.toBeNull()

    await act(async () => {
      await hook.current.submitTranscript('ship')
    })

    expect(hook.current.attemptError).toBeNull()
    expect(hook.current.status).toBe('feedback')
  })

  it('takes a recording failure reported by the screen', async () => {
    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()

    act(() => hook.current.reportAttemptError('Microphone access was denied.'))
    expect(hook.current.attemptError).toBe('Microphone access was denied.')

    // Moving on to the next word starts clean.
    act(() => hook.current.next())
    await settle()
    expect(hook.current.attemptError).toBeNull()
  })

  it('toggles the accent', async () => {
    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()

    expect(hook.current.accent).toBe('us')
    act(() => hook.current.setAccent('uk'))

    expect(hook.current.accent).toBe('uk')
  })

  it('advances to the next word', async () => {
    apiMock.submitPronunciationAttempt.mockResolvedValue({
      outcome: 'correct',
      score: 90,
      state: 'learning',
      streak: 1,
      familyDone: false,
    } satisfies PronunciationAttemptResult)

    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()
    await act(async () => {
      await hook.current.submitTranscript('ship')
    })

    apiMock.nextPronunciationWord.mockClear()
    act(() => hook.current.next())
    await settle()

    expect(apiMock.nextPronunciationWord).toHaveBeenCalledWith('ih-vs-iy', false)
    expect(hook.current.status).toBe('ready')
  })

  it('reports scoring while the attempt is in flight', async () => {
    let resolveAttempt: (r: PronunciationAttemptResult) => void = () => {}
    apiMock.submitPronunciationAttempt.mockReturnValue(new Promise((resolve) => (resolveAttempt = resolve)))

    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()
    let pending: Promise<void> = Promise.resolve()
    act(() => {
      pending = hook.current.submitTranscript('ship')
    })

    expect(hook.current.status).toBe('scoring')

    await act(async () => {
      resolveAttempt({ outcome: 'correct', score: 90, state: 'learning', streak: 1, familyDone: false })
      await pending
    })

    expect(hook.current.status).toBe('feedback')
  })

  it('fetches the family once and only the next word on advance', async () => {
    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()

    act(() => hook.current.next())
    await settle()

    expect(apiMock.getPronunciationFamily).toHaveBeenCalledTimes(1)
    expect(apiMock.nextPronunciationWord).toHaveBeenCalledTimes(2)
  })

  it('keeps the current word on screen while the next one loads', async () => {
    const hook = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()
    apiMock.nextPronunciationWord.mockReturnValue(new Promise(() => {}))

    act(() => hook.current.next())

    expect(hook.current.status).toBe('loading')
    expect(hook.current.word?.word).toBe('ship')
  })

  it('remembers the chosen accent for the next family', async () => {
    const first = await renderHook(() => usePronunciationFamily('ih-vs-iy'))
    await settle()
    act(() => first.current.setAccent('uk'))

    const second = await renderHook(() => usePronunciationFamily('th-sounds'))
    await settle()

    expect(second.current.accent).toBe('uk')
  })
})
