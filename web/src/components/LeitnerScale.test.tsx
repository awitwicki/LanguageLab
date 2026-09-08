import { describe, expect, it } from 'vitest'
import type { LearningProgress } from '../api/client'
import { render } from '../test/render'
import { LeitnerScale } from './LeitnerScale'

const progress: LearningProgress = { notStarted: 49, boxes: [9, 5, 0, 3, 0], learned: 12, total: 78 }

describe('LeitnerScale', () => {
  it('compact: сегменти пропорційні, нульові не рендеряться, відсоток праворуч', async () => {
    const { container } = await render(<LeitnerScale progress={progress} />)

    const root = container.querySelector('.leitner')!
    expect(root.classList.contains('leitner-compact')).toBe(true)

    const segments = [...root.querySelectorAll<HTMLElement>('.leitner-seg')]
    expect(segments.map((s) => s.className)).toEqual([
      'leitner-seg leitner-learned',
      'leitner-seg leitner-box4',
      'leitner-seg leitner-box2',
      'leitner-seg leitner-box1',
      'leitner-seg leitner-new',
    ])
    expect(segments[0].style.flexBasis.startsWith('15.38')).toBe(true)
    expect(segments[4].style.flexBasis.startsWith('62.82')).toBe(true)

    expect(root.querySelector('.leitner-percent')?.textContent).toBe('23%')
    expect(root.querySelector('.leitner-legend')).toBeNull()
  })

  it('aria-label називає відсоток і всі шість лічильників', async () => {
    const { container } = await render(<LeitnerScale progress={progress} />)

    expect(container.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe(
      'Вивчено 23%: вивчено — 12, бокс 4 — 3, бокс 3 — 0, бокс 2 — 5, бокс 1 — 9, не почато — 49',
    )
  })

  it('large: відсоток зверху й легенда з шести пунктів, нульові пригашені', async () => {
    const { container } = await render(<LeitnerScale progress={progress} size="large" />)

    expect(container.querySelector('.leitner-large .leitner-percent')?.textContent).toBe('23% вивчено')

    const items = [...container.querySelectorAll('.leitner-legend li')]
    expect(items).toHaveLength(6)
    expect(items.map((li) => li.textContent?.trim())).toEqual([
      'вивчено 12', 'бокс 4 3', 'бокс 3 0', 'бокс 2 5', 'бокс 1 9', 'не почато 49',
    ])
    expect(items.map((li) => li.classList.contains('is-empty'))).toEqual([false, false, true, false, false, false])
  })

  it('total 0 → нічого не рендерить', async () => {
    const { container } = await render(
      <LeitnerScale progress={{ notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }} />,
    )

    expect(container.innerHTML).toBe('')
  })
})
