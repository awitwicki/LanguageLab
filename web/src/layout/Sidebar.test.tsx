import { act } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { DictionaryListItem } from '../api/client'
import { click, render } from '../test/render'
import { Sidebar } from './Sidebar'

function choose(select: HTMLSelectElement, value: string) {
  return act(async () => {
    select.value = value
    select.dispatchEvent(new Event('change', { bubbles: true }))
  })
}

const items: DictionaryListItem[] = [
  { id: 1, name: 'Wool', wordsCount: 2000, sortedCount: 500, hasChapters: true, isPersonal: false },
  { id: 2, name: 'Dune', wordsCount: 100, sortedCount: 100, hasChapters: false, isPersonal: false },
  { id: 3, name: 'My words', wordsCount: 12, sortedCount: 12, hasChapters: false, isPersonal: true },
]

describe('Sidebar', () => {
  it('lists dictionaries with a percent, marks the active one and reports a click', async () => {
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
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const rows = [...container.querySelectorAll<HTMLButtonElement>('.sidebar-item:not(.personal-item)')]

    expect(rows.map((r) => r.querySelector('.name')?.textContent)).toEqual(['Wool', 'Dune'])
    expect(rows.map((r) => r.querySelector('.pct')?.textContent)).toEqual(['25%', '100%'])
    expect(rows[1].getAttribute('aria-current')).toBe('page')
    expect(rows[0].getAttribute('aria-current')).toBeNull()

    await click(rows[0])

    expect(onSelect).toHaveBeenCalledWith(1)
  })

  it('an empty list shows a hint, not "Loading…"', async () => {
    const { container } = await render(
      <Sidebar
        items={[]}
        error={null}
        activeId={null}
        importActive={false}
        canImport
        onSelect={() => {}}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    expect(container.textContent).toContain('No dictionaries yet')
    expect(container.textContent).not.toContain('Loading')
  })

  it('the import button turns primary while the import screen is open', async () => {
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
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const button = container.querySelector<HTMLButtonElement>('.sidebar-footer .btn')!

    expect(button.classList.contains('btn-primary')).toBe(true)
    // Phones show the short label; the accessible name stays the full one either way.
    expect(button.getAttribute('aria-label')).toBe('Import a book')
    expect(button.querySelector('.import-label')?.textContent).toBe('Import a book')
    expect(button.querySelector('.import-label-short')?.textContent).toBe('Import')

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
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    expect(container.querySelector('.sidebar-footer')).toBeNull()
  })

  it('pins the personal dictionary in its own section with a word count', async () => {
    const onOpenPersonal = vi.fn()
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive={false}
        canImport={false}
        onSelect={() => {}}
        onImport={() => {}}
        personalActive
        onOpenPersonal={onOpenPersonal}
      />,
    )

    const personal = container.querySelector<HTMLButtonElement>('.personal-item')!

    expect(personal.querySelector('.name')?.textContent).toBe('My words')
    expect(personal.querySelector('.pct')?.textContent).toBe('12 words')
    expect(personal.getAttribute('aria-current')).toBe('page')

    const books = [...container.querySelectorAll('.sidebar-item:not(.personal-item) .name')]
    expect(books.map((b) => b.textContent)).toEqual(['Wool', 'Dune'])

    await click(personal)

    expect(onOpenPersonal).toHaveBeenCalledTimes(1)
  })

  it('shows the personal entry before the list has loaded', async () => {
    const { container } = await render(
      <Sidebar
        items={null}
        error={null}
        activeId={null}
        importActive={false}
        canImport={false}
        onSelect={() => {}}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    expect(container.querySelector('.personal-item .name')?.textContent).toBe('My words')
    expect(container.querySelector('.personal-item .pct')?.textContent).toBe('0 words')
  })
})

// Phones swap the lists for a single native picker (the swap itself is in the stylesheet).
describe('picker', () => {
  it('offers the personal entry and every book with its percent, the current book selected', async () => {
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={2}
        importActive={false}
        canImport
        onSelect={() => {}}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const select = container.querySelector<HTMLSelectElement>('.sidebar-picker select')!

    expect([...select.options].map((o) => o.textContent)).toEqual([
      'Choose a dictionary',
      'My words · 12 words',
      'Wool · 25%',
      'Dune · 100%',
    ])
    expect(select.value).toBe('2')
  })

  it('shows the placeholder when no dictionary is open, and picking one opens it', async () => {
    const onSelect = vi.fn()
    const onOpenPersonal = vi.fn()
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive={false}
        canImport={false}
        onSelect={onSelect}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={onOpenPersonal}
      />,
    )

    const select = container.querySelector<HTMLSelectElement>('.sidebar-picker select')!

    expect(select.value).toBe('')
    expect(select.options[0].disabled).toBe(true)

    await choose(select, '1')

    expect(onSelect).toHaveBeenCalledWith(1)
    expect(onOpenPersonal).not.toHaveBeenCalled()

    await choose(select, 'personal')

    expect(onOpenPersonal).toHaveBeenCalledTimes(1)
    expect(onSelect).toHaveBeenCalledTimes(1)
  })

  it('selects the personal entry on its own screen and during its exercises', async () => {
    const onItsScreen = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive={false}
        canImport={false}
        onSelect={() => {}}
        onImport={() => {}}
        personalActive
        onOpenPersonal={() => {}}
      />,
    )

    expect(onItsScreen.container.querySelector<HTMLSelectElement>('.sidebar-picker select')!.value).toBe('personal')

    // Training the personal words: the route carries the personal dictionary's id, not the flag.
    const training = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={3}
        importActive={false}
        canImport={false}
        onSelect={() => {}}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    expect(training.container.querySelector<HTMLSelectElement>('.sidebar-picker select')!.value).toBe('personal')
    expect(training.container.querySelector('.personal-item')!.getAttribute('aria-current')).toBe('page')
  })

  it('lists the personal entry before the dictionaries have loaded', async () => {
    const { container } = await render(
      <Sidebar
        items={null}
        error={null}
        activeId={null}
        importActive={false}
        canImport={false}
        onSelect={() => {}}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const select = container.querySelector<HTMLSelectElement>('.sidebar-picker select')!

    expect([...select.options].map((o) => o.textContent)).toEqual(['Choose a dictionary', 'My words · 0 words'])
  })
})

describe('modes', () => {
  it('has no Programs section — the modes live in the top bar', async () => {
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive={false}
        canImport
        onSelect={() => {}}
        onImport={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    expect(container.textContent).not.toContain('Programs')
    expect(container.textContent).not.toContain('Irregular verbs')
    expect(container.textContent).not.toContain('Pronunciation')
  })
})
