import { describe, expect, it } from 'vitest'
import { render } from '../test/render'
import { SortingProgress } from './SortingProgress'

describe('SortingProgress', () => {
  it('показує відсоток, кількість і залишок', async () => {
    const { container } = await render(
      <SortingProgress scope="Wool" title="Holston" sorted={500} total={2000} />,
    )

    expect(container.querySelector('.scope')?.textContent).toBe('Wool')
    expect(container.querySelector('h1')?.textContent).toBe('Holston')
    expect(container.querySelector('.percent')?.textContent).toBe('25%')
    expect(container.querySelector('.counts')?.textContent).toBe('500 з 2 000 слів, залишилось 1 500')
    expect(container.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('25')
  })

  it('коли все посортовано — 100% і «залишилось 0»', async () => {
    const { container } = await render(<SortingProgress scope="Wool" title="Уся книжка" sorted={7} total={7} />)

    expect(container.querySelector('.percent')?.textContent).toBe('100%')
    expect(container.querySelector('.counts')?.textContent).toBe('7 з 7 слів, залишилось 0')
  })
})
