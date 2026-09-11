import { act } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { CurrentUser } from '../api/client'
import { click, flush, render } from '../test/render'
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

type Handlers = Partial<Pick<Parameters<typeof AccountMenu>[0], 'onAdmin' | 'onSignOut' | 'onDeleteAccount'>>

function mount(user: CurrentUser, handlers: Handlers = {}) {
  return render(
    <AccountMenu
      user={user}
      onAdmin={handlers.onAdmin ?? vi.fn()}
      onSignOut={handlers.onSignOut ?? vi.fn()}
      onDeleteAccount={handlers.onDeleteAccount ?? vi.fn(() => Promise.resolve())}
    />,
  )
}

function pressEscape() {
  return act(async () => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
  })
}

describe('AccountMenu', () => {
  it('stays closed until the button is pressed', async () => {
    const { container } = await mount(member)

    expect(container.querySelector('.account')?.getAttribute('aria-expanded')).toBe('false')
    expect(container.querySelector('.account-panel')).toBeNull()

    await click(container.querySelector('.account')!)

    expect(container.querySelector('.account')?.getAttribute('aria-expanded')).toBe('true')
    expect(container.querySelector('.account-name')?.textContent).toBe('Bo Lind')
  })

  it('hides the admin panel entry from a regular user', async () => {
    const { container } = await mount(member)

    await click(container.querySelector('.account')!)

    expect(container.querySelector('.to-admin')).toBeNull()
  })

  it('offers the admin panel to an admin', async () => {
    const onAdmin = vi.fn()
    const { container } = await mount(admin, { onAdmin })

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.to-admin')!)

    expect(onAdmin).toHaveBeenCalledOnce()
  })

  it('signs out and closes', async () => {
    const onSignOut = vi.fn()
    const { container } = await mount(admin, { onSignOut })

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.sign-out')!)

    expect(onSignOut).toHaveBeenCalledOnce()
    expect(container.querySelector('.account-panel')).toBeNull()
  })

  // Irreversible, so one click must never be enough — the first only arms the second.
  it('asks for confirmation before deleting the account', async () => {
    const onDeleteAccount = vi.fn(() => Promise.resolve())
    const { container } = await mount(member, { onDeleteAccount })

    await click(container.querySelector('.account')!)
    expect(container.querySelector('.delete-account')?.textContent).toBe('Delete account')
    expect(container.querySelector('.delete-warning')).toBeNull()

    await click(container.querySelector('.delete-account')!)

    expect(onDeleteAccount).not.toHaveBeenCalled()
    expect(container.querySelector('.delete-account')?.textContent).toBe('Confirm deletion')
    expect(container.querySelector('.delete-warning')?.textContent).toBe(
      'This removes your progress and cannot be undone.',
    )
    expect(container.querySelector('.account-panel')).not.toBeNull()
  })

  it('deletes on the second click', async () => {
    const onDeleteAccount = vi.fn(() => Promise.resolve())
    const { container } = await mount(member, { onDeleteAccount })

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.delete-account')!)
    await click(container.querySelector('.delete-account')!)

    expect(onDeleteAccount).toHaveBeenCalledOnce()
  })

  // The server's reason is written for the user (the last-admin rule), so it is shown as is,
  // and the menu stays open so it can actually be read.
  it('shows why a deletion was refused and stays open', async () => {
    const onDeleteAccount = vi.fn(() =>
      Promise.reject(new Error('This is the last administrator — promote someone else first.')),
    )
    const { container } = await mount(admin, { onDeleteAccount })

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.delete-account')!)
    await click(container.querySelector('.delete-account')!)
    await flush()

    expect(container.querySelector('.account-panel')).not.toBeNull()
    expect(container.querySelector('.error')?.textContent).toBe(
      'This is the last administrator — promote someone else first.',
    )
    expect(container.querySelector('.delete-account')?.textContent).toBe('Delete account')
  })

  // Arming the delete and then dismissing the menu must not leave a live "Confirm" waiting
  // for an accidental click the next time it opens.
  it('forgets a pending confirmation when the menu closes', async () => {
    const onDeleteAccount = vi.fn(() => Promise.resolve())
    const { container } = await mount(member, { onDeleteAccount })

    await click(container.querySelector('.account')!)
    await click(container.querySelector('.delete-account')!)
    await pressEscape()
    expect(container.querySelector('.account-panel')).toBeNull()

    await click(container.querySelector('.account')!)

    expect(container.querySelector('.delete-account')?.textContent).toBe('Delete account')
    expect(container.querySelector('.delete-warning')).toBeNull()
  })
})
