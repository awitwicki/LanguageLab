import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DictionaryDetail, LearningProgress, TrainingStarted } from '../api/client'
import { click, flush, render } from '../test/render'
import { DictionaryScreen } from './DictionaryScreen'

const apiMock = vi.hoisted(() => ({ getDictionary: vi.fn(), startReview: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const noLearning: LearningProgress = { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }
// notStarted == learnableCount (42) — так само, як на сервері. (9 + 10 + 12 + 60) / (5 · 71) → 26%.
const holstonLearning: LearningProgress = { notStarted: 42, boxes: [9, 5, 0, 3, 0], learned: 12, total: 71 }

const detail: DictionaryDetail = {
  id: 7,
  name: 'Wool',
  wordsCount: 2000,
  sortedCount: 500,
  learnableCount: 42,
  dueCount: 3,
  learning: holstonLearning,
  chapters: [
    { id: 11, order: 0, title: 'Holston', wordsCount: 300, sortedCount: 150, learnableCount: 42, learning: holstonLearning },
    { id: 12, order: 1, title: '', wordsCount: 100, sortedCount: 100, learnableCount: 0, learning: noLearning },
  ],
  topWords: [
    { wordPairId: 1, word: 'silo', frequency: 1500 },
    { wordPairId: 2, word: 'abide', frequency: 750 },
  ],
}

const reviewStarted: TrainingStarted = { trainingId: 9, mode: 'review', words: [], totalQuestions: 12 }

function screen(overrides: Partial<Parameters<typeof DictionaryScreen>[0]> = {}) {
  return <DictionaryScreen id={7} onSort={() => {}} onTrain={() => {}} onReview={() => {}} {...overrides} />
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.getDictionary.mockResolvedValue(detail)
  apiMock.startReview.mockResolvedValue(reviewStarted)
})

describe('DictionaryScreen — глави', () => {
  it('клік по назві глави сортує лише її; «Вправа» тренує лише її', async () => {
    const onSort = vi.fn()
    const onTrain = vi.fn()
    const { container } = await render(screen({ onSort, onTrain }))
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    expect(rows).toHaveLength(2)
    expect(rows[0].querySelector('.chapter-title')?.textContent).toBe('Holston')
    expect(rows[0].querySelector('.chapter-sub')?.textContent).toBe('300 слів · 42 до вивчення')
    expect(rows[0].querySelector('.chapter-pct')?.textContent).toBe('50%')
    expect(container.querySelector('input[type="checkbox"]')).toBeNull()

    await click(rows[0].querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith([11], 'Holston')

    await click(rows[0].querySelector('.chapter-train')!)
    expect(onTrain).toHaveBeenCalledWith([11], 'Holston')
  })

  it('глава без назви підписується номером; без слів до вивчення «Вправа» неактивна', async () => {
    const onSort = vi.fn()
    const { container } = await render(screen({ onSort }))
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    expect(rows[1].querySelector('.chapter-title')?.textContent).toBe('Глава 2')
    expect(rows[1].classList.contains('done')).toBe(true)
    expect((rows[1].querySelector('.chapter-train') as HTMLButtonElement).disabled).toBe(true)

    await click(rows[1].querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledWith([12], 'Глава 2')
  })

  it('плаский словник не має секції глав', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, chapters: [] })
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.chapter-list')).toBeNull()
    expect(container.textContent).toContain('плаский словник')
  })

  it('третій рядок глави — шкала Leitner; у главі без слів «не знаю» його немає', async () => {
    const { container } = await render(screen())
    await flush()

    const rows = [...container.querySelectorAll('.chapter-row')]
    const scale = rows[0].querySelector('.chapter-learning .leitner')!
    expect(scale).not.toBeNull()
    expect(scale.querySelector('.leitner-percent')?.textContent).toBe('26%')
    // Шкала — поза кнопкою сортування: її aria-label не має засмічувати назву кнопки.
    expect(rows[0].querySelector('.chapter-main .leitner')).toBeNull()
    expect(rows[1].querySelector('.chapter-learning')).toBeNull()
  })

  it('дві колонки: глави ліворуч (перші в DOM), найчастіші слова праворуч', async () => {
    const { container } = await render(screen())
    await flush()

    const columns = container.querySelector('.dict-columns')!
    expect(columns.children).toHaveLength(2)
    expect(columns.children[0].querySelector('.chapter-list')).not.toBeNull()
    expect(columns.children[1].querySelector('.top-words')).not.toBeNull()
  })
})

describe('DictionaryScreen — дії', () => {
  it('«Сортувати всю книжку» → onSort(null, «Уся книжка»)', async () => {
    const onSort = vi.fn()
    const { container } = await render(screen({ onSort }))
    await flush()

    await click(buttons(container).find((b) => b.textContent?.includes('Сортувати всю книжку'))!)

    expect(onSort).toHaveBeenCalledWith(null, 'Уся книжка')
  })

  it('«Почати вправу» → onTrain(null, «Уся книжка»)', async () => {
    const onTrain = vi.fn()
    const { container } = await render(screen({ onTrain }))
    await flush()

    const start = buttons(container).find((b) => b.textContent?.includes('Почати вправу'))!
    expect(start.disabled).toBe(false)

    await click(start)

    expect(onTrain).toHaveBeenCalledWith(null, 'Уся книжка')
  })

  it('без слів до вивчення «Почати вправу» неактивна і є підказка', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, learnableCount: 0 })
    const { container } = await render(screen())
    await flush()

    expect(buttons(container).find((b) => b.textContent?.includes('Почати вправу'))!.disabled).toBe(true)
    expect(container.textContent).toContain('Немає слів до вивчення')
  })

  it('«Повторити (3)» є при dueCount > 0 і запускає повторення', async () => {
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Повторити (3)')!)
    await flush()

    expect(apiMock.startReview).toHaveBeenCalledTimes(1)
    expect(onReview).toHaveBeenCalledWith(reviewStarted)
  })

  it('без прострочених кнопки повторення немає', async () => {
    apiMock.getDictionary.mockResolvedValue({ ...detail, dueCount: 0 })
    const { container } = await render(screen())
    await flush()

    expect(buttons(container).find((b) => b.textContent?.startsWith('Повторити'))).toBeUndefined()
  })

  it('у хедері — шкала книжки з підписом «Вивчено»; без слів «не знаю» її немає', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.dict-header .dict-learning .leitner-percent')?.textContent).toBe('26%')
    expect(container.querySelector('.dict-learning')?.textContent).toContain('Вивчено')

    apiMock.getDictionary.mockResolvedValue({ ...detail, learning: noLearning })
    const empty = await render(screen())
    await flush()

    expect(empty.container.querySelector('.dict-learning')).toBeNull()
  })

  it('повторення без слів (204) → повідомлення, onReview не викликається', async () => {
    apiMock.startReview.mockResolvedValue(null)
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Повторити (3)')!)
    await flush()

    expect(onReview).not.toHaveBeenCalled()
    expect(container.textContent).toContain('повторювати нічого')
  })
})

describe('DictionaryScreen — статистика', () => {
  it('показує топ слів за спаданням частоти з номерами', async () => {
    const { container } = await render(screen())
    await flush()

    const rows = [...container.querySelectorAll('.top-word')]
    expect(rows.map((r) => r.querySelector('.rank')?.textContent)).toEqual(['1', '2'])
    expect(rows.map((r) => r.querySelector('.word')?.textContent)).toEqual(['silo', 'abide'])
    expect(rows.map((r) => r.querySelector('.count')?.textContent)).toEqual(['1 500', '750'])
    expect((rows[1].querySelector('.bar') as HTMLElement).style.width).toBe('50%')
  })

  it('заголовок містить назву, кількість слів і глав', async () => {
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('h1')?.textContent).toBe('Wool')
    expect(container.querySelector('.dict-meta')?.textContent).toBe('2 000 слів, 2 глави')
  })
})
