import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DictionaryDetail } from '../api/client'
import { click, flush, render } from '../test/render'
import { DictionaryScreen } from './DictionaryScreen'

const apiMock = vi.hoisted(() => ({ getDictionary: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const detail: DictionaryDetail = {
  id: 7,
  name: 'Wool',
  wordsCount: 2000,
  sortedCount: 500,
  chapters: [
    { id: 11, order: 0, title: 'Holston', wordsCount: 300, sortedCount: 150 },
    { id: 12, order: 1, title: '', wordsCount: 100, sortedCount: 100 },
  ],
  topWords: [
    { wordPairId: 1, word: 'silo', frequency: 1500 },
    { wordPairId: 2, word: 'abide', frequency: 750 },
  ],
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.getDictionary.mockResolvedValue(detail)
})

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

describe('DictionaryScreen', () => {
  it('клік по главі одразу запускає сортування лише цієї глави', async () => {
    const onSort = vi.fn()
    const { container } = await render(<DictionaryScreen id={7} onSort={onSort} />)
    await flush()

    const rows = [...container.querySelectorAll<HTMLButtonElement>('.chapter-row')]

    expect(rows).toHaveLength(2)
    expect(rows[0].querySelector('.chapter-title')?.textContent).toBe('Holston')
    expect(rows[0].querySelector('.chapter-pct')?.textContent).toBe('50%')
    expect(container.querySelector('input[type="checkbox"]')).toBeNull()

    await click(rows[0])

    expect(onSort).toHaveBeenCalledWith([11], 'Holston')
  })

  it('глава без назви підписується порядковим номером і теж сортується', async () => {
    const onSort = vi.fn()
    const { container } = await render(<DictionaryScreen id={7} onSort={onSort} />)
    await flush()

    const rows = [...container.querySelectorAll<HTMLButtonElement>('.chapter-row')]

    expect(rows[1].querySelector('.chapter-title')?.textContent).toBe('Глава 2')
    expect(rows[1].classList.contains('done')).toBe(true)

    await click(rows[1])

    expect(onSort).toHaveBeenCalledWith([12], 'Глава 2')
  })

  it('«Сортувати всю книжку» передає null і заголовок книжки', async () => {
    const onSort = vi.fn()
    const { container } = await render(<DictionaryScreen id={7} onSort={onSort} />)
    await flush()

    const whole = buttons(container).find((b) => b.textContent?.includes('Сортувати всю книжку'))!

    await click(whole)

    expect(onSort).toHaveBeenCalledWith(null, 'Уся книжка')
  })

  it('показує топ слів за спаданням частоти з номерами', async () => {
    const { container } = await render(<DictionaryScreen id={7} onSort={() => {}} />)
    await flush()

    const rows = [...container.querySelectorAll('.top-word')]

    expect(rows.map((r) => r.querySelector('.rank')?.textContent)).toEqual(['1', '2'])
    expect(rows.map((r) => r.querySelector('.word')?.textContent)).toEqual(['silo', 'abide'])
    expect(rows.map((r) => r.querySelector('.count')?.textContent)).toEqual(['1 500', '750'])
    expect((rows[1].querySelector('.bar') as HTMLElement).style.width).toBe('50%')
  })

  it('«Почати вправу» є, але неактивна', async () => {
    const { container } = await render(<DictionaryScreen id={7} onSort={() => {}} />)
    await flush()

    const start = buttons(container).find((b) => b.textContent?.includes('Почати вправу'))!

    expect(start.disabled).toBe(true)
  })

  it('заголовок містить назву, кількість слів і глав', async () => {
    const { container } = await render(<DictionaryScreen id={7} onSort={() => {}} />)
    await flush()

    expect(container.querySelector('h1')?.textContent).toBe('Wool')
    expect(container.querySelector('.dict-meta')?.textContent).toBe('2 000 слів, 2 глави')
  })

  it('плаский словник не має секції глав', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, chapters: [] })
    const { container } = await render(<DictionaryScreen id={7} onSort={() => {}} />)
    await flush()

    expect(container.querySelector('.chapter-list')).toBeNull()
    expect(container.textContent).toContain('плаский словник')
  })
})
