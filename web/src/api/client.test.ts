import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, setUnauthorizedHandler } from './client'

/**
 * The import goes over XMLHttpRequest, not fetch, for the sake of upload progress — so the
 * stub is a tiny XHR: it records what was sent and lets the test drive progress and the reply.
 */
class FakeXhr {
  static instances: FakeXhr[] = []

  method = ''
  url = ''
  headers: Record<string, string> = {}
  body: string | null = null
  status = 0
  responseText = ''
  upload = { onprogress: null as ((event: ProgressEvent) => void) | null }
  onload: (() => void) | null = null
  onerror: (() => void) | null = null

  constructor() {
    FakeXhr.instances.push(this)
  }

  open(method: string, url: string) {
    this.method = method
    this.url = url
  }

  setRequestHeader(name: string, value: string) {
    this.headers[name] = value
  }

  send(body: string) {
    this.body = body
  }

  progress(loaded: number, total: number) {
    this.upload.onprogress?.({ lengthComputable: true, loaded, total } as ProgressEvent)
  }

  respond(status: number, text: string) {
    this.status = status
    this.responseText = text
    this.onload?.()
  }

  fail() {
    this.onerror?.()
  }
}

const payload = { name: 'Wool', words: [{ word: 'abide', count: 1 }], isPublic: false }
const result = { dictionaryId: 1, totalWords: 1, newWords: 1, reusedWords: 0 }

beforeEach(() => {
  FakeXhr.instances = []
  vi.stubGlobal('XMLHttpRequest', FakeXhr)
})

afterEach(() => {
  vi.unstubAllGlobals()
  setUnauthorizedHandler(() => {})
})

describe('importDictionary', () => {
  it('posts the book as JSON with the chosen visibility', async () => {
    const pending = api.importDictionary(payload)
    const [xhr] = FakeXhr.instances

    expect(xhr.method).toBe('POST')
    expect(xhr.url).toBe('/api/dictionaries/import')
    expect(xhr.headers['Content-Type']).toBe('application/json')
    expect(JSON.parse(String(xhr.body))).toMatchObject({ isPublic: false })

    xhr.respond(200, JSON.stringify(result))

    await expect(pending).resolves.toEqual(result)
  })

  it('reports the bytes sent while the upload is in flight', async () => {
    const seen: [number, number][] = []
    const pending = api.importDictionary(payload, (sent, total) => seen.push([sent, total]))
    const [xhr] = FakeXhr.instances

    xhr.progress(500, 2000)
    xhr.progress(2000, 2000)
    xhr.respond(200, JSON.stringify(result))

    await pending

    expect(seen).toEqual([
      [500, 2000],
      [2000, 2000],
    ])
  })

  it('shows the server’s own reason when it sends one', async () => {
    const pending = api.importDictionary(payload)

    FakeXhr.instances[0].respond(400, JSON.stringify({ message: 'The dictionary name cannot be empty.' }))

    await expect(pending).rejects.toThrow('The dictionary name cannot be empty.')
  })

  it('explains a 413 — the proxy in front of the API, not the API, refuses big books', async () => {
    const pending = api.importDictionary(payload)

    FakeXhr.instances[0].respond(413, '<html>413 Request Entity Too Large</html>')

    await expect(pending).rejects.toThrow(/too large.*413/)
  })

  it('falls back to the status code for any other failure', async () => {
    const pending = api.importDictionary(payload)

    FakeXhr.instances[0].respond(504, '<html>Gateway Time-out</html>')

    await expect(pending).rejects.toThrow('POST /api/dictionaries/import → 504')
  })

  it('names a dropped connection instead of hanging', async () => {
    const pending = api.importDictionary(payload)

    FakeXhr.instances[0].fail()

    await expect(pending).rejects.toThrow(/connection/i)
  })

  it('treats a 401 like every other request: the session is gone', async () => {
    const unauthorized = vi.fn()
    setUnauthorizedHandler(unauthorized)

    const pending = api.importDictionary(payload)

    FakeXhr.instances[0].respond(401, '')

    await expect(pending).rejects.toThrow()
    expect(unauthorized).toHaveBeenCalledTimes(1)
  })
})

describe('translateSentence', () => {
  afterEach(() => vi.unstubAllGlobals())

  const answer = (status: number, body: unknown = {}) =>
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status })))

  it('returns the translation', async () => {
    answer(200, { translation: 'Привіт.' })

    expect(await api.translateSentence('Hello.')).toEqual({ status: 'ok', translation: 'Привіт.' })
  })

  it('names the two limits apart from other failures', async () => {
    answer(429)
    expect(await api.translateSentence('Hello.')).toEqual({ status: 'limit' })

    answer(503, { reason: 'quota' })
    expect(await api.translateSentence('Hello.')).toEqual({ status: 'quota' })

    answer(502)
    expect(await api.translateSentence('Hello.')).toEqual({ status: 'failed' })
  })

  it('turns a network error into a failure', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('offline'))))

    expect(await api.translateSentence('Hello.')).toEqual({ status: 'failed' })
  })

  it('turns a malformed 200 body into a failure instead of throwing', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('not json', { status: 200 })))

    expect(await api.translateSentence('Hello.')).toEqual({ status: 'failed' })
  })
})
