import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { click, flush, render } from '../../test/render'
import { CardTask } from './CardTask'

const task: TaskDto = {
  id: 1,
  type: 'card',
  formAsked: 'recognition',
  level: 1,
  isReturn: false,
  verb: { v1: 'buy', translation: 'купувати', group: 3, family: 'ought', suffixes: ['ought', 'aught'] },
  card: {
    v2: ['bought'],
    v3: ['bought'],
    examples: [
      { tense: 'present', text: 'I [buy] fresh bread every day.' },
      { tense: 'past', text: 'She [bought] a new phone last week.' },
      { tense: 'perfect', text: 'We have [bought] a house.' },
    ],
    note: null,
  },
  gapChoice: null,
  oddOne: null,
  match: null,
  formPick: null,
  gapType: null,
  tripleType: null,
}

describe('CardTask', () => {
  it('shows the forms, translation, examples and a Got it button', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<CardTask task={task} disabled={false} onAnswer={onAnswer} />)
    await flush()

    expect(container.textContent).toContain('buy')
    expect(container.textContent).toContain('bought')
    expect(container.textContent).toContain('купувати')
    expect(container.textContent).toContain('She')

    await click(container.querySelector('button')!)
    expect(onAnswer).toHaveBeenCalledWith('seen')
  })

  it('highlights the family suffixes in the forms', async () => {
    const { container } = await render(<CardTask task={task} disabled={false} onAnswer={() => {}} />)
    await flush()

    const marks = [...container.querySelectorAll('mark')].map((m) => m.textContent)
    expect(marks).toContain('ought')
  })

  it('shows a note when the verb has one', async () => {
    const withNote = { ...task, verb: { ...task.verb, v1: 'read' }, card: { ...task.card!, note: 'Pronounced differently.' } }
    const { container } = await render(<CardTask task={withNote} disabled={false} onAnswer={() => {}} />)
    await flush()

    expect(container.textContent).toContain('Pronounced differently.')
  })
})
