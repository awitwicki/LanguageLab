import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { click, flush, render } from '../test/render'
import type { AuthState } from './useAuth'

const user = {
  id: 1,
  telegramUserId: 777,
  displayName: 'Ada',
  username: 'ada',
  photoUrl: null,
  role: 'admin' as const,
}

function respond(routes: Record<string, { status: number; body?: unknown }>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((path: string) => {
      const route = routes[path] ?? { status: 404 }

      return Promise.resolve({
        ok: route.status >= 200 && route.status < 300,
        status: route.status,
        json: () => Promise.resolve(route.body),
      } as Response)
    }),
  )
}

beforeEach(() => {
  window.history.replaceState({}, '', '/')
  vi.resetModules()
})
afterEach(() => vi.unstubAllGlobals())

describe('useAuth', () => {
  it('starts loading and settles on anonymous when there is no session', async () => {
    const { useAuth } = await import('./useAuth')
    respond({ '/api/auth/me': { status: 401 } })
    const seen: AuthState[] = []

    function Probe() {
      const { state } = useAuth()
      seen.push(state)
      return <span>{state.status}</span>
    }

    const { container } = await render(<Probe />)
    await flush()

    expect(seen[0].status).toBe('loading')
    expect(container.textContent).toBe('anonymous')
  })

  it('settles on signed-in when the cookie is still good', async () => {
    const { useAuth } = await import('./useAuth')
    respond({ '/api/auth/me': { status: 200, body: user } })
    const seen: AuthState[] = []

    function Probe() {
      const { state } = useAuth()
      seen.push(state)
      return <span>{state.status}</span>
    }

    await render(<Probe />)
    await flush()

    expect(seen.at(-1)).toEqual({ status: 'signed-in', user })
  })

  // A 401 from /api/auth/me is the normal signed-out answer, not an expiry: it must not
  // reach the unauthorized handler and cause a second state change.
  it('does not treat the boot probe as a dropped session', async () => {
    const { useAuth } = await import('./useAuth')
    respond({ '/api/auth/me': { status: 401 } })
    const seen: AuthState[] = []

    function Probe() {
      const { state } = useAuth()
      seen.push(state)
      return <span>{state.status}</span>
    }

    await render(<Probe />)
    await flush()

    expect(seen.filter((s) => s.status === 'anonymous')).toHaveLength(1)
  })

  it('drops to anonymous when another call returns 401', async () => {
    const { useAuth } = await import('./useAuth')
    const { api } = await import('../api/client')
    respond({ '/api/auth/me': { status: 200, body: user }, '/api/dictionaries': { status: 401 } })
    const seen: AuthState[] = []

    function Probe() {
      const { state } = useAuth()
      seen.push(state)
      return <span>{state.status}</span>
    }

    const { container } = await render(<Probe />)
    await flush()

    await expect(api.listDictionaries()).rejects.toThrow()
    await flush()

    expect(container.textContent).toBe('anonymous')
  })

  // `respond` keys on the path alone, and DELETE /api/auth/me shares its path with the boot
  // probe — so these two stub fetch by method as well.
  function respondByMethod(onDelete: { status: number; body?: unknown }) {
    vi.stubGlobal(
      'fetch',
      vi.fn((path: string, init?: RequestInit) => {
        const route =
          path !== '/api/auth/me' ? { status: 404 }
          : init?.method === 'DELETE' ? onDelete
          : { status: 200, body: user }

        return Promise.resolve({
          ok: route.status >= 200 && route.status < 300,
          status: route.status,
          json: () => Promise.resolve(route.body),
        } as Response)
      }),
    )
  }

  it('ends the session once the account is deleted', async () => {
    const { useAuth } = await import('./useAuth')
    respondByMethod({ status: 204 })

    function Probe() {
      const { state, deleteAccount } = useAuth()
      return (
        <button type="button" onClick={() => void deleteAccount()}>
          {state.status}
        </button>
      )
    }

    const { container } = await render(<Probe />)
    await flush()
    expect(container.textContent).toBe('signed-in')

    await click(container.querySelector('button')!)
    await flush()

    expect(container.textContent).toBe('anonymous')
  })

  // A refusal must surface its reason to the caller and leave the session exactly as it was.
  it('keeps the session and rethrows when the deletion is refused', async () => {
    const { useAuth } = await import('./useAuth')
    respondByMethod({
      status: 409,
      body: { message: 'This is the last administrator — promote someone else first.' },
    })
    const outcome: { error: Error | null } = { error: null }

    function Probe() {
      const { state, deleteAccount } = useAuth()
      return (
        <button
          type="button"
          onClick={() =>
            void deleteAccount().catch((e: Error) => {
              outcome.error = e
            })
          }
        >
          {state.status}
        </button>
      )
    }

    const { container } = await render(<Probe />)
    await flush()

    await click(container.querySelector('button')!)
    await flush()

    expect(outcome.error?.message).toBe('This is the last administrator — promote someone else first.')
    expect(container.textContent).toBe('signed-in')
  })

  // The OIDC callback is a redirect, so a refused login arrives in the URL, not a response body.
  it('reads a banned account out of the callback redirect and tidies the URL', async () => {
    window.history.replaceState({}, '', '/?error=banned')
    vi.resetModules()
    const { useAuth } = await import('./useAuth')
    respond({ '/api/auth/me': { status: 401 } })
    const seen: AuthState[] = []

    function Probe() {
      const { state } = useAuth()
      seen.push(state)
      return <span>{state.status}</span>
    }

    const { container } = await render(<Probe />)
    await flush()

    expect(container.textContent).toBe('banned')
    expect(window.location.search).toBe('')
  })

  it('reports a failed sign-in without leaving the login screen', async () => {
    window.history.replaceState({}, '', '/?error=login')
    vi.resetModules()
    const { useAuth } = await import('./useAuth')
    respond({ '/api/auth/me': { status: 401 } })

    function FailProbe() {
      const { state, loginFailed } = useAuth()
      return <span>{`${state.status}:${loginFailed}`}</span>
    }

    const { container } = await render(<FailProbe />)
    await flush()

    expect(container.textContent).toBe('anonymous:true')
    expect(window.location.search).toBe('')
  })

  // Regression test: StrictMode double-invokes effects. This ensures the URL read happens
  // at module scope, not inside the effect, so it only happens once even under double-invocation.
  it('maintains banned state under StrictMode double-invocation', async () => {
    window.history.replaceState({}, '', '/?error=banned')
    vi.resetModules()
    const { useAuth } = await import('./useAuth')
    respond({ '/api/auth/me': { status: 401 } })
    const seen: AuthState[] = []

    function Probe() {
      const { state } = useAuth()
      seen.push(state)
      return <span>{state.status}</span>
    }

    const { container } = await render(
      <StrictMode>
        <Probe />
      </StrictMode>,
    )
    await flush()

    // Should remain banned, not be overwritten to anonymous by the second invocation's getMe()
    expect(container.textContent).toBe('banned')
    expect(window.location.search).toBe('')
  })
})
