import { describe, expect, it, vi } from 'vitest'
import type { CurrentUser } from '../api/client'
import { click, render } from '../test/render'
import { AccountMenu } from './AccountMenu'

const member: CurrentUser = {
  id: 2,
  telegramUserId: 888,
  displayName: 'Bo Lind',
  username: 'bo',
  photoUrl: null,
  role: 'user',
}

const admin: CurrentUser = { ...member, id: 1, displayName: 'Ada Vance', role: 'admin' }

describe('AccountMenu', () => {
  it('stays closed until the button is pressed', async () => {
    const { container } = await render(
      <AccountMenu user={member} onAdmin={vi.fn()} onSignOut={vi.fn()} />,
    )

    expect(container.querySelector('.account')?.getAttribute('aria-expanded')).toBe('false')
    expect(container.querySelector('.account-panel')).toBeNull()

    await click(container.querySelector('.account')!)

    expect(container.querySelector('.account')?.getAttribute('aria-expanded')).toBe('true')
    expect(container.querySelector('.account-name')?.textContent).toBe('Bo Lind')
  })

  it('hides the admin panel entry from a regular user', async () => {
    const { container } = await render(
      <AccountMenu user={member} onAdmin={vi.fn()} onSignOut={vi.fn()} />,
    )

    await click(container.querySelector('.account')!)

    expect(container.querySelector('.to-admin')).toBeNull()
  })

  it('offers the admin panel to an admin', async () => {
    const onAdmin = vi.fn()
    const { container } = await render(
      <AccountMenu user={admin} onAdmin={onAdmin} onSignOut={vi.fn()} />,
    )

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.to-admin')!)

    expect(onAdmin).toHaveBeenCalledOnce()
  })

  it('signs out and closes', async () => {
    const onSignOut = vi.fn()
    const { container } = await render(
      <AccountMenu user={admin} onAdmin={vi.fn()} onSignOut={onSignOut} />,
    )

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.sign-out')!)

    expect(onSignOut).toHaveBeenCalledOnce()
    expect(container.querySelector('.account-panel')).toBeNull()
  })
})
