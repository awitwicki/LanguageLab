import { describe, expect, it } from 'vitest'
import { render } from '../test/render'
import { SortingProgress } from './SortingProgress'

describe('SortingProgress', () => {
  it('shows the percent, the count and what is left', async () => {
    const { container } = await render(
      <SortingProgress scope="Wool" title="Holston" sorted={500} total={2000} />,
    )

    expect(container.querySelector('.scope')?.textContent).toBe('Wool')
    expect(container.querySelector('h1')?.textContent).toBe('Holston')
    expect(container.querySelector('.percent')?.textContent).toBe('25%')
    expect(container.querySelector('.counts')?.textContent).toBe('500 of 2 000 words, 1 500 left')
    expect(container.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('25')
  })

  it('everything sorted — 100% and "0 left"', async () => {
    const { container } = await render(<SortingProgress scope="Wool" title="Whole book" sorted={7} total={7} />)

    expect(container.querySelector('.percent')?.textContent).toBe('100%')
    expect(container.querySelector('.counts')?.textContent).toBe('7 of 7 words, 0 left')
  })

  it('counts replaces the default caption under the bar', async () => {
    const { container } = await render(
      <SortingProgress scope="Wool" title="Holston" sorted={3} total={10} counts="Question 4 of 10" />,
    )

    expect(container.querySelector('.counts')?.textContent).toBe('Question 4 of 10')
    expect(container.querySelector('.percent')?.textContent).toBe('30%')
  })
})
