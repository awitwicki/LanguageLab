import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { click, flush, render } from '../../test/render'
import { OddOneTask } from './OddOneTask'

const task: TaskDto = {
  id: 1,
  type: 'oddOne',
  formAsked: 'recognition',
  level: 2,
  isReturn: false,
  verb: { v1: 'cut', translation: 'різати', group: 1, family: 'same', suffixes: [] },
  card: null,
  gapChoice: null,
  oddOne: { options: ['cut', 'put', 'hit', 'came'] },
  match: null,
  formPick: null,
  gapType: null,
  tripleType: null,
}

describe('OddOneTask', () => {
  it('asks which one is from another group and answers with the clicked option', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<OddOneTask task={task} disabled={false} onAnswer={onAnswer} />)
    await flush()

    expect(container.textContent).toContain('another group')
    const buttons = [...container.querySelectorAll('.option')]
    expect(buttons).toHaveLength(4)

    await click(buttons[3])
    expect(onAnswer).toHaveBeenCalledWith('came')
  })
})
