import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { parseLexicon } from './lexicon'

const SAMPLE = 'find\nfound find found\ngo\nwent go\n'

function textResponse(body: string, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(body),
  } as Response
}

// loadLexicon caches its promise at module level: every test imports a fresh module.
beforeEach(() => vi.resetModules())
afterEach(() => vi.unstubAllGlobals())

describe('parseLexicon', () => {
  it('reads a lone word as a lemma of itself', () => {
    const lexicon = parseLexicon(SAMPLE)

    expect(lexicon.lemmasOf('go')).toEqual(['go'])
    expect(lexicon.lemmaOf('go')).toBe('go')
  })

  it('reads a form followed by its lemmas, primary first', () => {
    const lexicon = parseLexicon(SAMPLE)

    expect(lexicon.lemmasOf('found')).toEqual(['find', 'found'])
    expect(lexicon.lemmaOf('found')).toBe('find')
    expect(lexicon.lemmasOf('went')).toEqual(['go'])
  })

  it('knows nothing it was not given and does not lowercase', () => {
    const lexicon = parseLexicon(SAMPLE)

    expect(lexicon.lemmasOf('aargh')).toEqual([])
    expect(lexicon.lemmaOf('aargh')).toBeNull()
    expect(lexicon.lemmaOf('Went')).toBeNull()
  })

  it('tolerates CRLF line endings and a missing final newline', () => {
    const lexicon = parseLexicon('go\r\nwent go')

    expect(lexicon.lemmaOf('went')).toBe('go')
  })

  it('refuses text that is not a lexicon, such as the SPA fallback page', () => {
    expect(() => parseLexicon('<!doctype html>\n<html lang="en">')).toThrow()
    expect(() => parseLexicon('')).toThrow()
  })
})

describe('loadLexicon', () => {
  it('fetches the file once and shares the result', async () => {
    const fetchMock = vi.fn<(url: string) => Promise<Response>>(() => Promise.resolve(textResponse(SAMPLE)))
    vi.stubGlobal('fetch', fetchMock)
    const { loadLexicon } = await import('./lexicon')

    const [first, second] = await Promise.all([loadLexicon(), loadLexicon()])
    const third = await loadLexicon()

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith('/lexicon/english-lexicon.txt')
    expect(second).toBe(first)
    expect(third).toBe(first)
    expect(first.lemmaOf('went')).toBe('go')
  })

  it('retries on the next call after a failed fetch', async () => {
    const fetchMock = vi
      .fn<(url: string) => Promise<Response>>()
      .mockImplementationOnce(() => Promise.reject(new TypeError('offline')))
      .mockImplementationOnce(() => Promise.resolve(textResponse(SAMPLE)))
    vi.stubGlobal('fetch', fetchMock)
    const { loadLexicon } = await import('./lexicon')

    await expect(loadLexicon()).rejects.toThrow('offline')
    const lexicon = await loadLexicon()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(lexicon.lemmaOf('went')).toBe('go')
  })

  it('treats an error status as a failure and retries', async () => {
    const fetchMock = vi
      .fn<(url: string) => Promise<Response>>()
      .mockImplementationOnce(() => Promise.resolve(textResponse('', 404)))
      .mockImplementationOnce(() => Promise.resolve(textResponse(SAMPLE)))
    vi.stubGlobal('fetch', fetchMock)
    const { loadLexicon } = await import('./lexicon')

    await expect(loadLexicon()).rejects.toThrow('404')
    const lexicon = await loadLexicon()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(lexicon.lemmaOf('found')).toBe('find')
  })

  it('treats a body that is not a lexicon as a failure and retries', async () => {
    const fetchMock = vi
      .fn<(url: string) => Promise<Response>>()
      .mockImplementationOnce(() => Promise.resolve(textResponse('<!doctype html>\n<html lang="en">')))
      .mockImplementationOnce(() => Promise.resolve(textResponse(SAMPLE)))
    vi.stubGlobal('fetch', fetchMock)
    const { loadLexicon } = await import('./lexicon')

    await expect(loadLexicon()).rejects.toThrow()
    const lexicon = await loadLexicon()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(lexicon.lemmaOf('went')).toBe('go')
  })
})
