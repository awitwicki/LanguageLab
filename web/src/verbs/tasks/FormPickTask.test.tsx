import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { click, flush, render } from '../../test/render'
import { FormPickTask } from './FormPickTask'

const task: TaskDto = {
  id: 1,
  type: 'formPick',
  formAsked: 'recognition',
  level: 3,
  isReturn: false,
  verb: { v1: 'go', translation: 'йти', group: 4, family: 'core', suffixes: [] },
  card: null,
  gapChoice: null,
  oddOne: null,
  match: null,
  formPick: { sentence: 'They [went] home early.' },
  gapType: null,
  tripleType: null,
}

describe('FormPickTask', () => {
  it('shows the sentence with the form underlined and two labelled buttons', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<FormPickTask task={task} disabled={false} onAnswer={onAnswer} />)
    await flush()

    const marked = container.querySelector('u')
    expect(marked?.textContent).toBe('went')
    expect(container.textContent).toContain('Past Simple')
    expect(container.textContent).toContain('Present Perfect')

    const buttons = [...container.querySelectorAll('.option')]
    await click(buttons[0])
    expect(onAnswer).toHaveBeenCalledWith('v2')

    await click(buttons[1])
    expect(onAnswer).toHaveBeenCalledWith('v3')
  })
})
