import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, setUnauthorizedHandler } from './client'

/**
 * The import goes over XMLHttpRequest, not fetch, for the sake of upload progress — so the
 * stub is a tiny XHR: it records what was sent and lets the test drive progress and the reply.
 */
class FakeXhr {
  static instances: FakeXhr[] = []
  status = 0
  responseText = ''
  upload = { onprogress: null as ((event: ProgressEvent) => void) | null }
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  private headers = new Map<string, string>()
  sent: unknown

  constructor() {
    FakeXhr.instances.push(this)
  }

  open() {}
  setRequestHeader() {}
  send(body: unknown) {
    this.sent = body
  }

  progress(loaded: number, total: number) {
    this.upload.onprogress?.({ lengthComputable: true, loaded, total } as ProgressEvent)
  }

  respond(status: number, body: unknown, headers: Record<string, string> = {}) {
    this.status = status
    this.responseText = typeof body === 'string' ? body : JSON.stringify(body)
    for (const [key, value] of Object.entries(headers)) this.headers.set(key.toLowerCase(), value)
    this.onload?.()
  }

  fail() {
    this.onerror?.()
  }

  getResponseHeader(name: string) {
    return this.headers.get(name.toLowerCase()) ?? null
  }
}

const RESULT = { dictionaryId: 1, totalWords: 1, newWords: 1, reusedWords: 0, droppedWords: 0, translationQueued: false }

beforeEach(() => {
  FakeXhr.instances = []
  vi.stubGlobal('XMLHttpRequest', FakeXhr)
})

afterEach(() => {
  vi.unstubAllGlobals()
  setUnauthorizedHandler(() => {})
})

describe('importDictionary', () => {
  it('sends the file as multipart form data, omitting chapterMode for leaf', async () => {
    const promise = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: true })
    const xhr = FakeXhr.instances[0]
    const body = xhr.sent as FormData

    expect(body.get('file')).toBeInstanceOf(File)
    expect(body.get('chapterMode')).toBeNull()
    expect(body.get('requestPublication')).toBe('true')

    xhr.respond(200, RESULT)
    await expect(promise).resolves.toMatchObject({ dictionaryId: 1 })
  })

  it('sends a numeric chapterMode when one is chosen', async () => {
    void api.importDictionary(new File(['x'], 'wool.fb2'), { chapterMode: 1, requestPublication: false })
    const body = FakeXhr.instances[0].sent as FormData

    expect(body.get('chapterMode')).toBe('1')
  })

  it('reports the bytes sent while the upload is in flight', async () => {
    const seen: [number, number][] = []
    const pending = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false }, (sent, total) =>
      seen.push([sent, total]),
    )
    const xhr = FakeXhr.instances[0]

    xhr.progress(500, 2000)
    xhr.progress(2000, 2000)
    xhr.respond(200, RESULT)

    await pending

    expect(seen).toEqual([
      [500, 2000],
      [2000, 2000],
    ])
  })

  it('shows the server’s own reason when it sends one', async () => {
    const pending = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })

    FakeXhr.instances[0].respond(400, { message: 'The dictionary name cannot be empty.' })

    await expect(pending).rejects.toThrow('The dictionary name cannot be empty.')
  })

  it('names the wait from the Retry-After header on a 429', async () => {
    const promise = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })
    FakeXhr.instances[0].respond(429, { message: 'quota' }, { 'Retry-After': '3600' })

    await expect(promise).rejects.toThrow('Try again in about 1 hour.')
  })

  // Review Focus: no header at all must still read, not crash or say "NaN".
  it('still gives a readable message on a 429 with no Retry-After header', async () => {
    const promise = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })
    FakeXhr.instances[0].respond(429, { message: 'quota' })

    await expect(promise).rejects.toThrow('You already imported a book today. Try again later.')
  })

  it('explains a 413 — the proxy in front of the API, not the API, refuses big books', async () => {
    const pending = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })

    FakeXhr.instances[0].respond(413, '<html>413 Request Entity Too Large</html>')

    await expect(pending).rejects.toThrow(/too large.*413/)
  })

  it('falls back to the status code for any other failure', async () => {
    const pending = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })

    FakeXhr.instances[0].respond(504, '<html>Gateway Time-out</html>')

    await expect(pending).rejects.toThrow('POST /api/dictionaries/import → 504')
  })

  it('names a dropped connection instead of hanging', async () => {
    const pending = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })

    FakeXhr.instances[0].fail()

    await expect(pending).rejects.toThrow(/connection/i)
  })

  it('treats a 401 like every other request: the session is gone', async () => {
    const unauthorized = vi.fn()
    setUnauthorizedHandler(unauthorized)

    const pending = api.importDictionary(new File(['x'], 'wool.fb2'), { requestPublication: false })

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

  it('names a sentence too long for the provider', async () => {
    answer(413)

    expect(await api.translateSentence('Hello.')).toEqual({ status: 'tooLong' })
  })
})
