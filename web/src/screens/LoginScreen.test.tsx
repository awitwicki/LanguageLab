import { describe, expect, it, vi } from 'vitest'
import { click, render } from '../test/render'
import { LoginScreen } from './LoginScreen'

describe('LoginScreen', () => {
  it('sends the browser to the OIDC entry point', async () => {
    const { container } = await render(<LoginScreen loginFailed={false} onTelegramSignIn={null} />)

    const link = container.querySelector('a.sign-in')

    // A full navigation, not a fetch: the handler answers with a redirect to Telegram.
    expect(link?.getAttribute('href')).toBe('/api/auth/telegram/start')
    expect(link?.textContent).toBe('Sign in with Telegram')
    expect(container.querySelector('.error')).toBeNull()
  })

  // Vite folds import.meta.env.DEV to a literal false in `npm run build` and drops the
  // branch, so this button cannot reach a production bundle. Vitest runs in dev mode, which
  // is why it is here at all — the assertion below pins that assumption.
  it('offers the local dev sign-in in a dev build', async () => {
    const { container } = await render(<LoginScreen loginFailed={false} onTelegramSignIn={null} />)

    expect(import.meta.env.DEV).toBe(true)

    const link = container.querySelector('a.dev-sign-in')

    expect(link?.getAttribute('href')).toBe('/api/auth/dev-login')
    expect(link?.textContent).toBe('Sign in as local dev')
  })

  it('explains a failed or cancelled sign-in', async () => {
    const { container } = await render(<LoginScreen loginFailed onTelegramSignIn={null} />)

    expect(container.querySelector('.error')?.textContent).toBe(
      'Sign-in did not complete. Please try again.',
    )
    expect(container.querySelector('a.sign-in')).not.toBeNull()
  })

  // Inside Telegram's web view the OIDC redirect has no way back, so the way in is a button
  // that repeats the launch-parameter sign-in — not a link that leaves the page.
  it('inside Telegram, signs in with a button instead of leaving for the OIDC flow', async () => {
    const onTelegramSignIn = vi.fn()
    const { container } = await render(<LoginScreen loginFailed={false} onTelegramSignIn={onTelegramSignIn} />)

    // The dev-only link shares the class (and vitest runs in dev mode); it is not the OIDC link.
    expect(container.querySelector('a.sign-in:not(.dev-sign-in)')).toBeNull()

    const button = container.querySelector('button.sign-in')!
    expect(button.textContent).toBe('Sign in with Telegram')

    await click(button)
    expect(onTelegramSignIn).toHaveBeenCalledTimes(1)
  })

  it('inside Telegram, a failed sign-in asks for a fresh launch', async () => {
    const { container } = await render(<LoginScreen loginFailed onTelegramSignIn={() => {}} />)

    expect(container.querySelector('.error')?.textContent).toBe(
      'Could not sign in through Telegram. Close this window and open the app from the bot again.',
    )
  })
})
