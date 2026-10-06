import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { click, flush, render } from './test/render'

const baseUser = {
  id: 1,
  telegramUserId: 777,
  displayName: 'Ada',
  username: 'ada',
  photoUrl: null,
  role: 'user' as const,
  suggestedLanguage: null as string | null,
  verbsWordCount: null as number | null,
  grammarGoal: 'A1',
}

const languages = [
  { code: 'uk', englishName: 'Ukrainian', nativeName: 'Українська' },
  { code: 'pl', englishName: 'Polish', nativeName: 'Polski' },
]

type Route = { status: number; body?: unknown }

// Same shape as useAuth.test.tsx's respond(), plus a route can be a function so a test can
// change what /api/auth/me answers after the picker "saves".
function respond(routes: Record<string, Route | (() => Route)>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string) => {
      const entry = routes[path]
      const route = (typeof entry === 'function' ? entry() : entry) ?? { status: 404 }

      return Promise.resolve({
        ok: route.status >= 200 && route.status < 300,
        status: route.status,
        json: () => Promise.resolve(route.body),
      } as Response)
    }),
  )
}

function dictionaryCalls() {
  return vi.mocked(fetch).mock.calls.filter(([path]) => path === '/api/dictionaries')
}

beforeEach(() => {
  window.history.replaceState({}, '', '/')
  vi.resetModules()
})
afterEach(() => vi.unstubAllGlobals())

describe('App', () => {
  // Fix 1 regression: /api/dictionaries requires a language and answers 409 until one is
  // picked, so the reload effect must not fire while the language picker is still up.
  it('does not reload the dictionary list while a brand-new user has no language yet', async () => {
    respond({
      '/api/auth/me': { status: 200, body: { ...baseUser, language: null } },
      '/api/languages': { status: 200, body: languages },
    })

    const App = (await import('./App')).default
    const { container } = await render(<App />)
    await flush()

    expect(dictionaryCalls()).toHaveLength(0)
    expect(container.querySelector('.language-picker-first-run')).not.toBeNull()
  })

  it('reloads the dictionary list once the user picks a language', async () => {
    let language: string | null = null
    respond({
      '/api/auth/me': () => ({ status: 200, body: { ...baseUser, language } }),
      '/api/languages': { status: 200, body: languages },
      '/api/auth/me/language': { status: 204 },
      '/api/dictionaries': { status: 200, body: [] },
    })

    const App = (await import('./App')).default
    const { container } = await render(<App />)
    await flush()

    expect(dictionaryCalls()).toHaveLength(0)

    language = 'uk'
    await click(container.querySelector('[data-code="uk"]')!)
    await click(container.querySelector('.btn-primary')!)
    await flush()

    expect(dictionaryCalls().length).toBeGreaterThan(0)
  })
})
