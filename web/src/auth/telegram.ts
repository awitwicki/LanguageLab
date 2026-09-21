/**
 * The only file that touches Telegram's Mini App bridge — window.Telegram.WebApp, defined by
 * telegram-web-app.js from index.html. Outside Telegram the script still defines the object,
 * with an empty initData, so every function here is safe to call anywhere.
 */
interface TelegramWebApp {
  /** The launch parameters Telegram signed for this page: a query string, empty outside Telegram. */
  initData: string
  ready(): void
  expand(): void
  /** Opens the URL in the phone's own browser, outside Telegram. */
  openLink(url: string): void
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
