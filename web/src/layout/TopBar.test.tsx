import { describe, expect, it, vi } from 'vitest'
import type { CurrentUser } from '../api/client'
import { click, render } from '../test/render'
import type { AppMode } from './mode'
import { TopBar } from './TopBar'

const user: CurrentUser = { id: 1, telegramUserId: 1, displayName: 'Ada', username: null, photoUrl: null, role: 'user' }

function topBar(mode: AppMode | null, onSelectMode: (mode: AppMode) => void = () => {}) {
  return (
    <TopBar
      user={user}
      mode={mode}
      onSelectMode={onSelectMode}
      onHome={() => {}}
      onAdmin={() => {}}
      onSignOut={() => {}}
      onDeleteAccount={() => Promise.resolve()}
    />
  )
}

function tabs(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('.mode-tab')]
}

describe('TopBar — mode tabs', () => {
  it('offers the three modes in order and marks the current one', async () => {
    const { container } = await render(topBar('pronunciation'))

    expect(tabs(container).map((t) => t.textContent)).toEqual(['Words', 'Pronunciation', 'Irregular verbs'])
    expect(tabs(container).map((t) => t.getAttribute('aria-current'))).toEqual([null, 'page', null])
  })

  it('marks no tab when the screen belongs to no mode', async () => {
    const { container } = await render(topBar(null))

    expect(tabs(container).map((t) => t.getAttribute('aria-current'))).toEqual([null, null, null])
  })

  it('reports the chosen mode', async () => {
    const onSelectMode = vi.fn()
    const { container } = await render(topBar('words', onSelectMode))

    await click(tabs(container)[2])

    expect(onSelectMode).toHaveBeenCalledWith('verbs')
  })
})
