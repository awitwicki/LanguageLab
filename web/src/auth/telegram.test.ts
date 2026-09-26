import { afterEach, describe, expect, it, vi } from 'vitest'
import { watchTelegramFullscreen } from './telegram'

/** A stand-in for Telegram's bridge, with the one event the watcher subscribes to. */
function webApp({ initData = 'auth_date=1&hash=abc', isFullscreen = false } = {}) {
  const handlers: (() => void)[] = []
  const app = {
    initData,
    isFullscreen,
    ready: vi.fn(),
    expand: vi.fn(),
    openLink: vi.fn(),
    onEvent: vi.fn((_event: string, handler: () => void) => void handlers.push(handler)),
    offEvent: vi.fn((_event: string, handler: () => void) => {
      handlers.splice(handlers.indexOf(handler), 1)
    }),
  }

  vi.stubGlobal('Telegram', { WebApp: app })
  return { app, fire: () => handlers.forEach((handler) => handler()) }
}

const marked = () => document.documentElement.dataset.tgFullscreen

afterEach(() => {
  vi.unstubAllGlobals()
  delete document.documentElement.dataset.tgFullscreen
})

describe('watchTelegramFullscreen', () => {
  it('marks the page when Telegram opened it full-screen', () => {
    webApp({ isFullscreen: true })

    watchTelegramFullscreen()

    expect(marked()).toBe('true')
  })

  it('leaves an ordinary Mini App alone', () => {
    webApp({ isFullscreen: false })

    watchTelegramFullscreen()

    expect(marked()).toBeUndefined()
  })

  it('does nothing outside Telegram, where the bridge has no launch parameters', () => {
    const { app } = webApp({ initData: '', isFullscreen: true })

    watchTelegramFullscreen()

    expect(marked()).toBeUndefined()
    expect(app.onEvent).not.toHaveBeenCalled()
  })

  it('follows the mode when it changes under the app', () => {
    const { app, fire } = webApp({ isFullscreen: false })

    watchTelegramFullscreen()
    app.isFullscreen = true
    fire()

    expect(marked()).toBe('true')

    app.isFullscreen = false
    fire()

    expect(marked()).toBeUndefined()
  })

  it('hands back a teardown that unsubscribes', () => {
    const { app } = webApp({ isFullscreen: true })

    watchTelegramFullscreen()()

    expect(app.offEvent).toHaveBeenCalledWith('fullscreenChanged', app.onEvent.mock.calls[0][1])
  })

  it('survives a client too old for the full-screen events', () => {
    vi.stubGlobal('Telegram', { WebApp: { initData: 'auth_date=1&hash=abc', ready: vi.fn(), expand: vi.fn() } })

    expect(() => watchTelegramFullscreen()()).not.toThrow()
    expect(marked()).toBeUndefined()
  })
})
