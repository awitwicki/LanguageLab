import { act } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../../api/client'
import { flush, render } from '../../test/render'
import { TripleTypeTask } from './TripleTypeTask'

const task: TaskDto = {
  id: 1,
  type: 'tripleType',
  formAsked: 'both',
  level: 3,
  isReturn: false,
  verb: { v1: 'buy', translation: 'купувати', group: 3, family: 'ought', suffixes: ['ought', 'aught'] },
  card: null,
  gapChoice: null,
  oddOne: null,
  match: null,
  formPick: null,
  gapType: null,
  tripleType: { v1: 'buy', autofillV3: true },
}

function setValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

describe('TripleTypeTask', () => {
  it('shows V1 and two labelled inputs and submits v2 pipe v3', async () => {
    const onAnswer = vi.fn()
    const { container } = await render(<TripleTypeTask task={{ ...task, tripleType: { v1: 'buy', autofillV3: false } }} disabled={false} onAnswer={onAnswer} />)
    await flush()

    expect(container.textContent).toContain('buy')
    const inputs = [...container.querySelectorAll<HTMLInputElement>('input')]
    expect(inputs).toHaveLength(2)

    await act(async () => setValue(inputs[0], 'bought'))
    await act(async () => setValue(inputs[1], 'bought'))

    const form = container.querySelector('form')!
    await act(async () => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })))

    expect(onAnswer).toHaveBeenCalledWith('bought|bought')
  })

  it('mirrors V2 into V3 while autofill is on', async () => {
    const { container } = await render(<TripleTypeTask task={task} disabled={false} onAnswer={() => {}} />)
    await flush()

    const inputs = [...container.querySelectorAll<HTMLInputElement>('input')]
    await act(async () => setValue(inputs[0], 'bought'))

    expect(inputs[1].value).toBe('bought')
  })

  it('does not mirror once autofill is off', async () => {
    const noAutofill = { ...task, tripleType: { v1: 'buy', autofillV3: false } }
    const { container } = await render(<TripleTypeTask task={noAutofill} disabled={false} onAnswer={() => {}} />)
    await flush()

    const inputs = [...container.querySelectorAll<HTMLInputElement>('input')]
    await act(async () => setValue(inputs[0], 'bought'))

    expect(inputs[1].value).toBe('')
  })
})
