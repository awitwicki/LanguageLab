import { useCallback, useEffect, useState } from 'react'
import { api, setUnauthorizedHandler, type CurrentUser } from '../api/client'

export type AuthState =
  | { status: 'loading' }
  | { status: 'anonymous' }
  | { status: 'banned' }
  | { status: 'signed-in'; user: CurrentUser }

/**
 * The OIDC callback is a redirect, so anything it needs to tell the SPA arrives as a query
 * parameter. Read it once and strip it, or a reload would replay a stale outcome.
 */
function takeCallbackError(): string | null {
  const error = new URLSearchParams(window.location.search).get('error')

  if (error) {
    window.history.replaceState({}, '', window.location.pathname)
  }

  return error
}

// Evaluated once, at module load — immune to React re-running the effect that reads it,
// which is exactly what StrictMode's dev-mode double-invocation does.
const initialCallbackError = takeCallbackError()

/**
 * The single source of "who is using the app". Mounted once at the root; the four states map
 * one-to-one onto the four things the shell can render.
 */
export function useAuth() {
  const [state, setState] = useState<AuthState>({ status: 'loading' })
  const [loginFailed] = useState(() => initialCallbackError === 'login')

  useEffect(() => {
    let cancelled = false

    if (initialCallbackError === 'banned') {
      setState({ status: 'banned' })
      return
    }

    api
      .getMe()
      .then((user) => {
        if (!cancelled) {
          setState(user ? { status: 'signed-in', user } : { status: 'anonymous' })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ status: 'anonymous' })
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  // A 401 from any other call means the session died under us — banned by an admin, signed
  // out in another tab, or simply expired. Show the login screen, not an error.
  useEffect(() => {
    setUnauthorizedHandler(() => setState({ status: 'anonymous' }))
  }, [])

  const signOut = useCallback(async () => {
    try {
      await api.logout()
    } finally {
      setState({ status: 'anonymous' })
    }
  }, [])

  /** The banned screen's way back: there is no session to end, only a message to leave. */
  const dismissBanned = useCallback(() => setState({ status: 'anonymous' }), [])

  return { state, loginFailed, signOut, dismissBanned }
}
