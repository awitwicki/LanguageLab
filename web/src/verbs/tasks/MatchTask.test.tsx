import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { click, flush, render } from '../../test/render'
import { MatchTask } from './MatchTask'

const task: TaskDto = {
  id: 1,
  type: 'match',
  formAsked: 'v2',
  level: 2,
  isReturn: false,
  verb: { v1: 'buy', translation: 'купувати', group: 3, family: 'ought', suffixes: ['ought', 'aught'] },
  card: null,
  gapChoice: null,
  oddOne: null,
  match: {
    form: 'v2',
    lefts: ['buy', 'bring', 'think'],
    rights: ['thought', 'bought', 'brought'],
    matched: [],
  },
  formPick: null,
  gapType: null,
  tripleType: null,
}

describe('MatchTask', () => {
  it('reports a pair when a left then a right are clicked', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<MatchTask task={task} disabled={false} onAnswer={onAnswer} />)
    await flush()

    await click(container.querySelector('[data-left="buy"]')!)
    await click(container.querySelector('[data-right="bought"]')!)

    expect(onAnswer).toHaveBeenCalledWith('buy=bought')
  })

  it('locks a matched left and its right so they cannot be clicked again', async () => {
    const matched = { ...task, match: { ...task.match!, matched: [{ left: 'buy', right: 'bought' }] } }
    const { container } = await render(<MatchTask task={matched} disabled={false} onAnswer={() => {}} />)
    await flush()

    const left = container.querySelector<HTMLButtonElement>('[data-left="buy"]')!
    const right = container.querySelector<HTMLButtonElement>('[data-right="bought"]')!
    expect(left.disabled).toBe(true)
    expect(right.disabled).toBe(true)
    expect(left.className).toContain('is-matched')
  })

  it('flashes the last wrong pair red', async () => {
    const { container } = await render(
      <MatchTask task={task} disabled={false} onAnswer={() => {}} lastWrongPair={{ left: 'buy', right: 'brought' }} />,
    )
    await flush()

    expect(container.querySelector('[data-left="buy"]')?.className).toContain('is-wrong')
    expect(container.querySelector('[data-right="brought"]')?.className).toContain('is-wrong')
  })
})
