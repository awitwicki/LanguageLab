import { describe, expect, it, vi } from 'vitest'
import type { ChapterView } from '../api/client'
import { click, render } from '../test/render'
import { ChapterRow } from './ChapterRow'

const holston: ChapterView = {
  id: 11,
  order: 0,
  title: 'Holston',
  wordsCount: 300,
  sortedCount: 150,
  learnableCount: 42,
  learning: { notStarted: 42, boxes: [9, 5, 0, 3, 0], learned: 12, total: 71 },
  dueCount: 0,
  nextDueAt: null,
  isStarred: false,
}

// Every word started, five due: the side button offers a review.
const juliette: ChapterView = {
  ...holston,
  id: 13,
  order: 2,
  title: 'Juliette',
  wordsCount: 80,
  sortedCount: 80,
  learnableCount: 0,
  learning: { notStarted: 0, boxes: [5, 4, 0, 0, 0], learned: 1, total: 10 },
  dueCount: 5,
}

function row(overrides: Partial<Parameters<typeof ChapterRow>[0]> = {}) {
  return (
    <ul className="chapter-list">
      <ChapterRow
        chapter={holston}
        reviewBusy={false}
        starBusy={false}
        onSort={() => {}}
        onTrain={() => {}}
        onReview={() => {}}
        onToggleStar={() => {}}
        {...overrides}
      />
    </ul>
  )
}

const star = (container: HTMLElement) => container.querySelector<HTMLButtonElement>('.chapter-star')!

describe('ChapterRow', () => {
  it('renders the label, the sub-line with the sorted share, and a labelled Leitner scale', async () => {
    const { container } = await render(row())

    expect(container.querySelector('.chapter-title')?.textContent).toBe('Holston')
    expect(container.querySelector('.chapter-sub')?.textContent).toBe('300 words · 50% sorted · 42 to learn')
    expect(container.querySelector('.chapter-learning .leitner-percent')?.textContent).toBe('26% learned')
    expect(container.querySelector('.chapter-row')?.classList.contains('done')).toBe(false)
  })

  it('the main area and "Sort" both sort; "Learn" trains', async () => {
    const onSort = vi.fn()
    const onTrain = vi.fn()
    const { container } = await render(row({ onSort, onTrain }))

    await click(container.querySelector('.chapter-main')!)
    expect(onSort).toHaveBeenCalledTimes(1)

    const sort = container.querySelector<HTMLButtonElement>('.chapter-sort')!
    expect(sort.textContent).toBe('Sort')
    expect(sort.getAttribute('aria-label')).toBe('Sort: Holston')
    await click(sort)
    expect(onSort).toHaveBeenCalledTimes(2)

    const train = container.querySelector<HTMLButtonElement>('.chapter-train')!
    expect(train.textContent).toBe('Learn')
    expect(train.getAttribute('aria-label')).toBe('Learn: Holston')
    await click(train)
    expect(onTrain).toHaveBeenCalledTimes(1)
  })

  it('a chapter with due words and nothing new offers "Review" instead, reads "all sorted" and drops "Sort"', async () => {
    const onReview = vi.fn()
    const { container } = await render(row({ chapter: juliette, onReview }))

    const train = container.querySelector<HTMLButtonElement>('.chapter-train')!
    expect(train.textContent).toBe('Review')
    expect(container.querySelector('.chapter-sub')?.textContent).toBe('80 words · all sorted · 5 to review')
    expect(container.querySelector('.chapter-row')?.classList.contains('done')).toBe(true)
    expect(container.querySelector('.chapter-sort')).toBeNull()

    await click(train)
    expect(onReview).toHaveBeenCalledTimes(1)
  })

  it('an unstarred chapter shows an empty star labelled "Star: …" that calls onToggleStar', async () => {
    const onToggleStar = vi.fn()
    const { container } = await render(row({ onToggleStar }))

    expect(star(container).textContent).toBe('☆')
    expect(star(container).getAttribute('aria-pressed')).toBe('false')
    expect(star(container).getAttribute('aria-label')).toBe('Star: Holston')

    await click(star(container))
    expect(onToggleStar).toHaveBeenCalledTimes(1)
  })

  it('a starred chapter shows a filled star labelled "Unstar: …"', async () => {
    const { container } = await render(row({ chapter: { ...holston, isStarred: true } }))

    expect(star(container).textContent).toBe('★')
    expect(star(container).getAttribute('aria-pressed')).toBe('true')
    expect(star(container).getAttribute('aria-label')).toBe('Unstar: Holston')
  })

  it('starBusy disables the star only; reviewBusy disables the review button only', async () => {
    const { container } = await render(row({ chapter: juliette, starBusy: true, reviewBusy: true }))

    expect(star(container).disabled).toBe(true)
    expect(container.querySelector<HTMLButtonElement>('.chapter-train')!.disabled).toBe(true)
    expect(container.querySelector<HTMLButtonElement>('.chapter-main')!.disabled).toBe(false)
  })

  it('compact drops the Leitner scale and marks the row', async () => {
    const { container } = await render(row({ compact: true }))

    expect(container.querySelector('.chapter-learning')).toBeNull()
    expect(container.querySelector('.chapter-row')?.classList.contains('compact')).toBe(true)
  })

  it('the scale stays outside the sort button so it does not pollute its accessible name', async () => {
    const { container } = await render(row())

    expect(container.querySelector('.chapter-main .leitner')).toBeNull()
  })
})
