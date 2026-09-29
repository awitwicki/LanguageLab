import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LearnerLanguageContext } from '../account/learnerLanguage'
import { flush, render } from '../test/render'
import { MemoryBookStore } from './bookStore'
import type { SentenceTranslation } from './Sentence'
import { useSentenceTranslations } from './useSentenceTranslations'

const apiMock = vi.hoisted(() => ({ translateSentence: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const HASH = 'a'.repeat(64)
const KEY = '1.2.0'
const TEXT = 'Most men tried to adjust.'

function defer<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

let hook: {
  translations: Record<string, SentenceTranslation>
  toggle: (key: string, text: string) => Promise<void>
} | null = null

const store = new MemoryBookStore()

function Probe() {
  hook = useSentenceTranslations(HASH, store)
  return null
}

beforeEach(() => {
  apiMock.translateSentence.mockReset()
})

describe('useSentenceTranslations', () => {
  it('does not reopen a translation that was closed while its request was still in flight', async () => {
    // A request whose resolution we control by hand, so we can close the translation mid-flight.
    const deferred = defer<{ status: 'ok'; translation: string }>()
    apiMock.translateSentence.mockReturnValue(deferred.promise)

    await render(<Probe />)

    // Start loading: the device cache misses (MemoryBookStore is empty), so it falls through to
    // the still-pending server request.
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await flush()

    expect(hook!.translations[KEY]).toEqual({ state: 'loading' })

    // Close it — a second toggle on a loading key closes it, same as the reader's UI does on a tap.
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await flush()

    expect(hook!.translations[KEY]).toBeUndefined()

    // Now the in-flight request finally resolves. Its continuation must recognize it was
    // superseded by the close and must not silently reopen the translation.
    await act(async () => {
      deferred.resolve({ status: 'ok', translation: 'Більшість чоловіків намагалися пристосуватися.' })
    })
    await flush()

    expect(hook!.translations[KEY]).toBeUndefined()
  })

  // Fix 2 regression: the cache key must include the learner's language, or a translation made
  // in one language would be served — mislabeled — when the learner reads in another.
  it('keeps translations of different learner languages from cross-contaminating', async () => {
    apiMock.translateSentence
      .mockResolvedValueOnce({ status: 'ok', translation: 'Українською.' })
      .mockResolvedValueOnce({ status: 'ok', translation: 'Po polsku.' })

    const view = await render(
      <LearnerLanguageContext.Provider value="uk">
        <Probe />
      </LearnerLanguageContext.Provider>,
    )
    await flush()

    // Translate under Ukrainian.
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await flush()
    expect(hook!.translations[KEY]).toEqual({ state: 'open', text: 'Українською.' })

    // Close it, then switch the learner's language and translate the same sentence again — a
    // cache miss must happen, not the Ukrainian text back with a Polish label.
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await view.rerender(
      <LearnerLanguageContext.Provider value="pl">
        <Probe />
      </LearnerLanguageContext.Provider>,
    )
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await flush()

    expect(hook!.translations[KEY]).toEqual({ state: 'open', text: 'Po polsku.' })
    expect(apiMock.translateSentence).toHaveBeenCalledTimes(2)

    // Close, then switch back to Ukrainian — its own cached translation must still be there,
    // and reopening it must not call the server a third time (nor return the Polish text).
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await view.rerender(
      <LearnerLanguageContext.Provider value="uk">
        <Probe />
      </LearnerLanguageContext.Provider>,
    )
    await act(async () => {
      void hook!.toggle(KEY, TEXT)
    })
    await flush()

    expect(hook!.translations[KEY]).toEqual({ state: 'open', text: 'Українською.' })
    expect(apiMock.translateSentence).toHaveBeenCalledTimes(2)
  })

  it('names the wait when the translation limit is hit', async () => {
    apiMock.translateSentence.mockResolvedValue({ status: 'limit', retryAfterSeconds: 7 })

    await render(<Probe />)
    await act(async () => {
      void hook!.toggle('9.9.9', 'A sentence nobody cached.')
    })
    await flush()

    expect(hook!.translations['9.9.9']).toEqual({
      state: 'error',
      message: 'Too many translations at once. Try again in 7 seconds.',
    })
  })
})
