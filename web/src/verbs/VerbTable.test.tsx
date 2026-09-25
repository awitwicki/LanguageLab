import { describe, expect, it } from 'vitest'
import type { VerbRow } from '../api/client'
import { render } from '../test/render'
import { VerbTable } from './VerbTable'

function row(v1: string, extra: Partial<VerbRow> = {}): VerbRow {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `пер-${v1}`,
    group: 1,
    mastery: 0,
    streak: 0,
    passed: false,
    answers: 0,
    ...extra,
  }
}

describe('VerbTable', () => {
  it('shows every verb with its three forms and translation', async () => {
    const { container } = await render(<VerbTable verbs={[row('cut'), row('put')]} />)

    expect(container.querySelectorAll('tbody tr')).toHaveLength(2)
    expect(container.textContent).toContain('cut')
    expect(container.textContent).toContain('cut-2')
    expect(container.textContent).toContain('cut-3')
    expect(container.textContent).toContain('пер-put')
  })

  it('paints the bar by band and sizes it by mastery', async () => {
    const { container } = await render(
      <VerbTable verbs={[row('cut', { mastery: 0.9, answers: 5 }), row('put')]} />,
    )

    const [strong, untouched] = [...container.querySelectorAll<HTMLElement>('.mastery-fill')]

    expect(strong.dataset.band).toBe('top')
    expect(strong.style.width).toBe('90%')
    expect(untouched.dataset.band).toBe('none')
    expect(untouched.style.width).toBe('0%')
  })

  it('reads out the level for a verb that has been answered', async () => {
    const { container } = await render(<VerbTable verbs={[row('cut', { mastery: 0.5, answers: 4 })]} />)

    expect(container.textContent).toContain('50%')
  })

  it('leaves the level blank until there is an answer behind it', async () => {
    const { container } = await render(<VerbTable verbs={[row('cut')]} />)

    expect(container.textContent).not.toContain('0%')
  })

  it('marks a passed verb', async () => {
    const { container } = await render(
      <VerbTable verbs={[row('cut', { passed: true, streak: 4, mastery: 0.9, answers: 5 })]} />,
    )

    expect(container.querySelector('.verb-passed')).not.toBeNull()
  })
})
