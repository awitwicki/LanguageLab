import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { click, flush, render } from '../../test/render'
import { GapChoiceTask } from './GapChoiceTask'

const task: TaskDto = {
  id: 1,
  type: 'gapChoice',
  formAsked: 'v2',
  level: 2,
  isReturn: false,
  verb: { v1: 'cut', translation: 'різати', group: 1, family: 'same', suffixes: [] },
  card: null,
  gapChoice: { sentence: 'Yesterday I ___ my finger.', tense: 'past', options: ['wore', 'cut', 'cat', 'cutted'] },
  oddOne: null,
  match: null,
  formPick: null,
  gapType: null,
  tripleType: null,
}

describe('GapChoiceTask', () => {
  it('shows the sentence with numbered option buttons', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<GapChoiceTask task={task} disabled={false} onAnswer={onAnswer} />)
    await flush()

    expect(container.textContent).toContain('Yesterday I ___ my finger.')
    const buttons = [...container.querySelectorAll('.option')]
    expect(buttons.map((b) => b.textContent)).toEqual(
      expect.arrayContaining(['1wore', '2cut', '3cat', '4cutted'].map((_, i) => expect.stringContaining(task.gapChoice!.options[i]))),
    )

    await click(buttons[1])
    expect(onAnswer).toHaveBeenCalledWith('cut')
  })

  it('disables the buttons while disabled', async () => {
    const { container } = await render(<GapChoiceTask task={task} disabled={true} onAnswer={() => {}} />)
    await flush()

    const buttons = [...container.querySelectorAll<HTMLButtonElement>('.option')]
    expect(buttons.every((b) => b.disabled)).toBe(true)
  })
})
