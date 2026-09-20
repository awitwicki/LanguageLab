import { useCallback, useEffect, useState } from 'react'
import { api, setUnauthorizedHandler, type CurrentUser, type WebAppLogin } from '../api/client'
import { telegramInitData } from './telegram'

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

// Also read once: Telegram's bridge defines it before any module runs, and it does not change
// for the life of the page.
const initialInitData = telegramInitData()

/**
 * The single source of "who is using the app". Mounted once at the root; the four states map
 * one-to-one onto the four things the shell can render.
 */
export function useAuth() {
  const [state, setState] = useState<AuthState>({ status: 'loading' })
  const [loginFailed, setLoginFailed] = useState(() => initialCallbackError === 'login')

  // One Mini App sign-in's outcome, whether the boot probe made it or the login screen's button.
  const applyWebAppLogin = useCallback((login: WebAppLogin) => {
    if (login.status === 'failed') {
      setLoginFailed(true)
      setState({ status: 'anonymous' })
      return
    }

    setLoginFailed(false)
    setState(login)
  }, [])

  useEffect(() => {
    let cancelled = false

    if (initialCallbackError === 'banned') {
      setState({ status: 'banned' })
      return
    }

    api
      .getMe()
      .then(async (user) => {
        if (cancelled) {
          return
        }

        if (user) {
          setState({ status: 'signed-in', user })
          return
        }

        // Inside Telegram there is a second way in: the launch parameters Telegram signed for
        // this page. The cookie is asked first, so a web view kept open past their 24-hour
        // window keeps working on the session it already has.
        if (!initialInitData) {
          setState({ status: 'anonymous' })
          return
        }

        const login = await api.telegramWebAppLogin(initialInitData)

        if (!cancelled) {
          applyWebAppLogin(login)
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
  }, [applyWebAppLogin])

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

  /**
   * Unlike signOut, a refusal here leaves the session as it was and rethrows: the server's
   * reason is meant for the user, and the menu shows it.
   */
  const deleteAccount = useCallback(async () => {
    await api.deleteMe()
    setState({ status: 'anonymous' })
  }, [])

  /** The banned screen's way back: there is no session to end, only a message to leave. */
  const dismissBanned = useCallback(() => setState({ status: 'anonymous' }), [])

  /** The login screen's button inside Telegram: the sign-in the boot probe made, on demand. */
  const signInWithTelegram = useCallback(async () => {
    if (!initialInitData) {
      return
    }

    applyWebAppLogin(await api.telegramWebAppLogin(initialInitData))
  }, [applyWebAppLogin])

  return {
    state,
    loginFailed,
    insideTelegram: initialInitData !== null,
    signInWithTelegram,
    signOut,
    deleteAccount,
    dismissBanned,
  }
}
