import { act } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { flush, render } from '../../test/render'
import { GapTypeTask } from './GapTypeTask'

const task: TaskDto = {
  id: 1,
  type: 'gapType',
  formAsked: 'v3',
  level: 3,
  isReturn: false,
  verb: { v1: 'go', translation: 'йти', group: 4, family: 'core', suffixes: [] },
  card: null,
  gapChoice: null,
  oddOne: null,
  match: null,
  formPick: null,
  gapType: { sentence: 'She has ___ to the shop.', tense: 'perfect', hint: 'go' },
  tripleType: null,
}

describe('GapTypeTask', () => {
  it('shows the sentence and hint, submits the typed value on Enter', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<GapTypeTask task={task} disabled={false} onAnswer={onAnswer} hint={null} />)
    await flush()

    expect(container.textContent).toContain('She has ___ to the shop.')
    expect(container.textContent).toContain('go')

    const input = container.querySelector<HTMLInputElement>('input')!
    const setValue = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
    await act(async () => {
      setValue.call(input, 'gone')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })

    const form = container.querySelector('form')!
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    })

    expect(onAnswer).toHaveBeenCalledWith('gone')
  })

  it('shows an inline hint when one is given', async () => {
    const { container } = await render(<GapTypeTask task={task} disabled={false} onAnswer={() => {}} hint="Almost — check the spelling" />)
    await flush()

    expect(container.textContent).toContain('Almost — check the spelling')
  })
})
