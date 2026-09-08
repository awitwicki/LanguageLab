import { describe, expect, it, vi } from 'vitest'
import type { DictionaryListItem } from '../api/client'
import { click, render } from '../test/render'
import { Sidebar } from './Sidebar'

const items: DictionaryListItem[] = [
  { id: 1, name: 'Wool', wordsCount: 2000, sortedCount: 500, hasChapters: true },
  { id: 2, name: 'Dune', wordsCount: 100, sortedCount: 100, hasChapters: false },
]

describe('Sidebar', () => {
  it('показує словники з відсотком, позначає активний і віддає клік', async () => {
    const onSelect = vi.fn()
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={2}
        importActive={false}
        canImport
        onSelect={onSelect}
        onImport={() => {}}
      />,
    )

    const rows = [...container.querySelectorAll<HTMLButtonElement>('.sidebar-item')]

    expect(rows.map((r) => r.querySelector('.name')?.textContent)).toEqual(['Wool', 'Dune'])
    expect(rows.map((r) => r.querySelector('.pct')?.textContent)).toEqual(['25%', '100%'])
    expect(rows[1].getAttribute('aria-current')).toBe('page')
    expect(rows[0].getAttribute('aria-current')).toBeNull()

    await click(rows[0])

    expect(onSelect).toHaveBeenCalledWith(1)
  })

  it('порожній список — підказка, а не «Завантажую…»', async () => {
    const { container } = await render(
      <Sidebar
        items={[]}
        error={null}
        activeId={null}
        importActive={false}
        canImport
        onSelect={() => {}}
        onImport={() => {}}
      />,
    )

    expect(container.textContent).toContain('Поки жодного словника')
    expect(container.textContent).not.toContain('Завантажую')
  })

  it('кнопка імпорту стає primary, коли відкритий екран імпорту', async () => {
    const onImport = vi.fn()
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive
        canImport
        onSelect={() => {}}
        onImport={onImport}
      />,
    )

    const button = container.querySelector<HTMLButtonElement>('.sidebar-footer .btn')!

    expect(button.classList.contains('btn-primary')).toBe(true)

    await click(button)

    expect(onImport).toHaveBeenCalledTimes(1)
  })

  it('hides the import button from a user who cannot create dictionaries', async () => {
    const { container } = await render(
      <Sidebar
        items={[]}
        error={null}
        activeId={null}
        importActive={false}
        canImport={false}
        onSelect={vi.fn()}
        onImport={vi.fn()}
      />,
    )

    expect(container.querySelector('.sidebar-footer')).toBeNull()
  })
})
