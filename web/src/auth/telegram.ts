/**
 * The only file that touches Telegram's Mini App bridge — window.Telegram.WebApp, defined by
 * telegram-web-app.js from index.html. Outside Telegram the script still defines the object,
 * with an empty initData, so every function here is safe to call anywhere.
 */
interface TelegramWebApp {
  /** The launch parameters Telegram signed for this page: a query string, empty outside Telegram. */
  initData: string
  /** Bot API 8.0. Undefined in older clients, which never run a Mini App full-screen. */
  isFullscreen?: boolean
  ready(): void
  expand(): void
  /** Opens the URL in the phone's own browser, outside Telegram. */
  openLink(url: string): void
  onEvent?(event: 'fullscreenChanged', handler: () => void): void
  offEvent?(event: 'fullscreenChanged', handler: () => void): void
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TelegramWebApp }
  }
}

/** The signed launch parameters, or null when the page is not running inside Telegram. */
export function telegramInitData(): string | null {
  const data = window.Telegram?.WebApp?.initData

  return data ? data : null
}

/**
 * Tells Telegram the page has rendered (it keeps its own placeholder up until then) and asks
 * for the full height. Nothing happens outside Telegram.
 */
export function markTelegramReady(): void {
  const webApp = window.Telegram?.WebApp

  if (!webApp || !telegramInitData()) {
    return
  }

  webApp.ready()
  webApp.expand()
}

/** Hands the URL to the phone's browser, leaving the Mini App. Nothing happens outside Telegram. */
export function openOutsideTelegram(url: string): void {
  const webApp = window.Telegram?.WebApp

  if (!webApp || !telegramInitData()) {
    return
  }

  webApp.openLink(url)
}

/**
 * Full-screen Telegram — a Mini App opened from a home-screen icon — hands the page the whole
 * screen, so the phone's status bar and Telegram's own floating Close and menu buttons are drawn
 * over the top of it. Telegram keeps the room they take in --tg-safe-area-inset-* and
 * --tg-content-safe-area-inset-*, which it sets on <html> a moment after the first paint; this
 * marks the mode itself, and index.css turns the two into the --safe-top / --safe-bottom the top
 * bar and the reader pad themselves by. Nothing happens outside Telegram. Returns the teardown.
 */
export function watchTelegramFullscreen(): () => void {
  const webApp = window.Telegram?.WebApp

  if (!webApp || !telegramInitData()) {
    return () => {}
  }

  const apply = () => {
    if (webApp.isFullscreen) {
      document.documentElement.dataset.tgFullscreen = 'true'
    } else {
      delete document.documentElement.dataset.tgFullscreen
    }
  }

  apply()
  webApp.onEvent?.('fullscreenChanged', apply)

  return () => webApp.offEvent?.('fullscreenChanged', apply)
}
