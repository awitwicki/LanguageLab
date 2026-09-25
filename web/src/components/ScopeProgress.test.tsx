import { describe, expect, it } from 'vitest'
import type { LearningProgress } from '../api/client'
import { render } from '../test/render'
import { ScopeProgress } from './ScopeProgress'

// (9 + 10 + 12 + 60) / (5 · 71) → 26%.
const learning: LearningProgress = { notStarted: 42, boxes: [9, 5, 0, 3, 0], learned: 12, total: 71 }
const nothing: LearningProgress = { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 }

describe('ScopeProgress', () => {
  it('a book: a "Sorted" row with the percent, a "Learned" row with the scale, both captions', async () => {
    const { container } = await render(<ScopeProgress sorting={{ sorted: 500, total: 2000 }} learning={learning} />)

    const rows = [...container.querySelectorAll('.scope-row')]
    expect(rows.map((r) => r.querySelector('.scope-label')?.textContent)).toEqual(['Sorted', 'Learned'])
    expect(rows[0].querySelector('.progress-fill')?.getAttribute('style')).toContain('width: 25%')
    expect(rows[0].querySelector('.scope-value')?.textContent).toBe('25%')
    expect(rows[1].querySelector('.leitner-percent')?.textContent).toBe('26%')

    const captions = [...container.querySelectorAll('.scope-caption')].map((c) => c.textContent)
    expect(captions[0]).toBe('500 of 2 000 words sorted')
    expect(captions[1]).toContain('box 1')
  })

  it('a book nobody has trained yet: the sorted row alone, no scale and no learned caption', async () => {
    const { container } = await render(<ScopeProgress sorting={{ sorted: 500, total: 2000 }} learning={nothing} />)

    expect(container.querySelectorAll('.scope-row')).toHaveLength(1)
    expect(container.querySelector('.leitner')).toBeNull()
    expect(container.querySelectorAll('.scope-caption')).toHaveLength(1)
  })

  it('a scope that is never sorted (the personal dictionary): the learned row alone', async () => {
    const { container } = await render(<ScopeProgress learning={learning} />)

    expect([...container.querySelectorAll('.scope-label')].map((l) => l.textContent)).toEqual(['Learned'])
    expect(container.querySelector('.progress')).toBeNull()
    expect(container.querySelectorAll('.scope-caption')).toHaveLength(1)
  })

  it('nothing to show → renders nothing', async () => {
    const { container } = await render(<ScopeProgress learning={nothing} />)

    expect(container.innerHTML).toBe('')
  })
})
