import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from './client'

afterEach(() => vi.unstubAllGlobals())

describe('importDictionary', () => {
  it('sends the chosen visibility to the server', async () => {
    const fetchMock = vi.fn(() =>
      Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve({ dictionaryId: 1, totalWords: 1, newWords: 1, reusedWords: 0 }),
      } as Response),
    )

    vi.stubGlobal('fetch', fetchMock)

    await api.importDictionary({ name: 'Wool', words: [{ word: 'abide', count: 1 }], isPublic: false })

    const call = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    const body = JSON.parse(String(call[1].body)) as { isPublic: boolean }

    expect(body.isPublic).toBe(false)
  })
})
