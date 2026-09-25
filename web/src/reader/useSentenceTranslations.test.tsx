import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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
})
