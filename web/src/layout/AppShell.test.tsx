import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import type { CurrentUser } from '../api/client'
import { render } from '../test/render'
import { AppShell } from './AppShell'

const user: CurrentUser = { id: 1, telegramUserId: 1, displayName: 'Ada', username: null, photoUrl: null, role: 'user' }

function shell(screenKey: string, children: ReactNode = 'content') {
  return (
    <AppShell
      user={user}
      screenKey={screenKey}
      sidebar={<nav />}
      onHome={() => {}}
      onAdmin={() => {}}
      onSignOut={() => {}}
      onDeleteAccount={() => Promise.resolve()}
    >
      {children}
    </AppShell>
  )
}

describe('AppShell — focus on screen change', () => {
  it('the first screen does not steal focus', async () => {
    const { container } = await render(shell('home'))

    expect(document.activeElement).not.toBe(container.querySelector('main'))
  })

  it('a new screen moves focus to the main region so assistive tech announces it', async () => {
    const { container, rerender } = await render(shell('home'))
    await rerender(shell('dictionary:7', 'other'))

    expect(document.activeElement).toBe(container.querySelector('main'))
  })

  it('a re-render of the same screen leaves focus where the user put it', async () => {
    const { container, rerender } = await render(shell('home', <button type="button">stay</button>))
    container.querySelector('button')!.focus()

    await rerender(shell('home', <button type="button">stay</button>))

    expect(document.activeElement).toBe(container.querySelector('button'))
  })
})
