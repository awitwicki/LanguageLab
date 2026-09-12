import { describe, expect, it, vi } from 'vitest'
import type { DictionaryListItem } from '../api/client'
import { click, render } from '../test/render'
import { Sidebar } from './Sidebar'

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
        verbsLearnedPercent={0}
        verbsActive={false}
        onOpenVerbs={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const rows = [...container.querySelectorAll<HTMLButtonElement>('.sidebar-item:not(.program-item):not(.personal-item)')]

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
        verbsLearnedPercent={0}
        verbsActive={false}
        onOpenVerbs={() => {}}
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
        verbsLearnedPercent={0}
        verbsActive={false}
        onOpenVerbs={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
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
        verbsLearnedPercent={0}
        verbsActive={false}
        onOpenVerbs={() => {}}
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
        verbsLearnedPercent={0}
        verbsActive={false}
        onOpenVerbs={() => {}}
        personalActive
        onOpenPersonal={onOpenPersonal}
      />,
    )

    const personal = container.querySelector<HTMLButtonElement>('.personal-item')!

    expect(personal.querySelector('.name')?.textContent).toBe('My words')
    expect(personal.querySelector('.pct')?.textContent).toBe('12 words')
    expect(personal.getAttribute('aria-current')).toBe('page')

    const books = [...container.querySelectorAll('.sidebar-item:not(.program-item):not(.personal-item) .name')]
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
        verbsLearnedPercent={0}
        verbsActive={false}
        onOpenVerbs={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    expect(container.querySelector('.personal-item .name')?.textContent).toBe('My words')
    expect(container.querySelector('.personal-item .pct')?.textContent).toBe('0 words')
  })
})

describe('the Programs section', () => {
  it('shows the irregular-verbs entry with its step progress', async () => {
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive={false}
        canImport
        onSelect={() => {}}
        onImport={() => {}}
        verbsLearnedPercent={25}
        verbsActive={false}
        onOpenVerbs={() => {}}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const entry = container.querySelector<HTMLButtonElement>('.program-item')!

    expect(entry.textContent).toContain('Irregular verbs')
    expect(entry.textContent).toContain('25% learned')
    expect(entry.getAttribute('aria-current')).toBeNull()
  })

  it('marks the entry active and reports a click', async () => {
    const onOpenVerbs = vi.fn()
    const { container } = await render(
      <Sidebar
        items={items}
        error={null}
        activeId={null}
        importActive={false}
        canImport
        onSelect={() => {}}
        onImport={() => {}}
        verbsLearnedPercent={0}
        verbsActive
        onOpenVerbs={onOpenVerbs}
        personalActive={false}
        onOpenPersonal={() => {}}
      />,
    )

    const entry = container.querySelector<HTMLButtonElement>('.program-item')!
    expect(entry.getAttribute('aria-current')).toBe('page')

    await click(entry)

    expect(onOpenVerbs).toHaveBeenCalledTimes(1)
  })
})
