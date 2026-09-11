import { describe, expect, it } from 'vitest'
import type { LearningProgress } from '../api/client'
import { render } from '../test/render'
import { LeitnerScale } from './LeitnerScale'

const progress: LearningProgress = { notStarted: 49, boxes: [9, 5, 0, 3, 0], learned: 12, total: 78 }

describe('LeitnerScale', () => {
  it('compact: proportional segments, empty ones not rendered, percent on the right', async () => {
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

  it('aria-label names the percent and all six counters', async () => {
    const { container } = await render(<LeitnerScale progress={progress} />)

    expect(container.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe(
      'Learned 23%: learned — 12, box 4 — 3, box 3 — 0, box 2 — 5, box 1 — 9, not started — 49',
    )
  })

  it('large: percent on top and a six-item legend, empty ones dimmed', async () => {
    const { container } = await render(<LeitnerScale progress={progress} size="large" />)

    expect(container.querySelector('.leitner-large .leitner-percent')?.textContent).toBe('23% learned')

    const items = [...container.querySelectorAll('.leitner-legend li')]
    expect(items).toHaveLength(6)
    expect(items.map((li) => li.textContent?.trim())).toEqual([
      'learned 12', 'box 4 3', 'box 3 0', 'box 2 5', 'box 1 9', 'not started 49',
    ])
    expect(items.map((li) => li.classList.contains('is-empty'))).toEqual([false, false, true, false, false, false])
  })

  it('total 0 → renders nothing', async () => {
    const { container } = await render(
      <LeitnerScale progress={{ notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }} />,
    )

    expect(container.innerHTML).toBe('')
  })
})
