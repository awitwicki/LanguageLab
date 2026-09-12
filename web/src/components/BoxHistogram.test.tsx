import { describe, expect, it } from 'vitest'
import { render } from '../test/render'
import { BoxHistogram } from './BoxHistogram'

describe('BoxHistogram', () => {
  it('one bar per box, scaled to the tallest, with the count above and the box label below', async () => {
    const { container } = await render(<BoxHistogram boxCounts={[12, 6, 0, 3, 1]} />)

    const bars = [...container.querySelectorAll<HTMLElement>('.boxes-bar')]
    expect(bars).toHaveLength(5)
    expect(bars.map((b) => b.style.height)).toEqual(['100%', '50%', '0%', '25%', '8.33%'])

    const counts = [...container.querySelectorAll('.boxes-count')].map((c) => c.textContent)
    expect(counts).toEqual(['12', '6', '0', '3', '1'])

    const labels = [...container.querySelectorAll('.boxes-label')].map((l) => l.textContent)
    expect(labels).toEqual(['Box 1', 'Box 2', 'Box 3', 'Box 4', 'Box 5'])
  })

  it('aria-label summarises every box', async () => {
    const { container } = await render(<BoxHistogram boxCounts={[1200, 0, 0, 0, 2]} />)

    expect(container.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe(
      'Words by box: box 1 — 1 200, box 2 — 0, box 3 — 0, box 4 — 0, box 5 — 2',
    )
  })

  it('all boxes empty → every bar is 0%, nothing divides by zero', async () => {
    const { container } = await render(<BoxHistogram boxCounts={[0, 0, 0, 0, 0]} />)

    const bars = [...container.querySelectorAll<HTMLElement>('.boxes-bar')]
    expect(bars.map((b) => b.style.height)).toEqual(['0%', '0%', '0%', '0%', '0%'])
  })
})
