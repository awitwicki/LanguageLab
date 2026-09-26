import { describe, expect, it, vi } from 'vitest'
import { click, render } from '../test/render'
import { ScopeRow } from './ScopeRow'

function row(overrides: Partial<Parameters<typeof ScopeRow>[0]> = {}) {
  return (
    <ul className="scope-list">
      <ScopeRow title="Wool · Holston" sub="38% sorted" action="Sort" onAction={() => {}} {...overrides} />
    </ul>
  )
}

const buttons = (container: HTMLElement) => [...container.querySelectorAll<HTMLButtonElement>('button')]

describe('ScopeRow', () => {
  it('shows the scope and its line', async () => {
    const { container } = await render(row())

    expect(container.querySelector('.scope-title')?.textContent).toBe('Wool · Holston')
    expect(container.querySelector('.scope-sub')?.textContent).toBe('38% sorted')
  })

  /// Both the row and the labelled button do the one thing the row is for.
  it('acts from the row and from the button alike', async () => {
    const onAction = vi.fn()
    const { container } = await render(row({ onAction }))

    await click(container.querySelector('.scope-main')!)
    expect(onAction).toHaveBeenCalledTimes(1)

    await click(container.querySelector('.scope-action')!)
    expect(onAction).toHaveBeenCalledTimes(2)
  })

  /// A screen reader hears which scope the button belongs to, not three identical "Repeat"s.
  it('names the scope in the action label', async () => {
    const { container } = await render(row({ action: 'Repeat' }))

    expect(container.querySelector('.scope-action')?.getAttribute('aria-label')).toBe('Repeat: Wool · Holston')
  })

  it('goes quiet while busy', async () => {
    const onAction = vi.fn()
    const { container } = await render(row({ busy: true, onAction }))

    expect(buttons(container).every((b) => b.disabled)).toBe(true)

    await click(container.querySelector('.scope-action')!)
    expect(onAction).not.toHaveBeenCalled()
  })
})
