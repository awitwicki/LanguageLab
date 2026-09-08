import { describe, expect, it } from 'vitest'
import { render } from '../test/render'
import { LoginScreen } from './LoginScreen'

describe('LoginScreen', () => {
  it('sends the browser to the OIDC entry point', async () => {
    const { container } = await render(<LoginScreen loginFailed={false} />)

    const link = container.querySelector('a.sign-in')

    // A full navigation, not a fetch: the handler answers with a redirect to Telegram.
    expect(link?.getAttribute('href')).toBe('/api/auth/telegram/start')
    expect(link?.textContent).toBe('Sign in with Telegram')
    expect(container.querySelector('.error')).toBeNull()
  })

  it('explains a failed or cancelled sign-in', async () => {
    const { container } = await render(<LoginScreen loginFailed />)

    expect(container.querySelector('.error')?.textContent).toBe(
      'Sign-in did not complete. Please try again.',
    )
    expect(container.querySelector('a.sign-in')).not.toBeNull()
  })
})
