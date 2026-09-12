import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GradeResult, IrregularVerbSession, VerbForm } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbSessionScreen } from './VerbSessionScreen'

const apiMock = vi.hoisted(() => ({ getVerbSession: vi.fn(), gradeVerbForm: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

const session: IrregularVerbSession = {
  step: 4,
  title: 'All three forms differ',
  review: false,
  cards: [{ v1: 'go', v2: 'went', v3: 'gone', translation: 'йти', open: 'v2' }],
}

const gradeResult: GradeResult = {
  verb: 'go',
  learned: false,
  forms: (['v1', 'v2', 'v3'] as VerbForm[]).map((form) => ({ form, state: 'learning', streak: 1, correct: 1, wrong: 0 })),
}

function press(key: string) {
  return act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  })
}

const states = (container: HTMLElement) =>
  [...container.querySelectorAll('.verb-card')].map((c) => c.getAttribute('data-state'))

beforeEach(() => {
  apiMock.getVerbSession.mockReset()
  apiMock.gradeVerbForm.mockReset()
  apiMock.getVerbSession.mockResolvedValue(session)
  apiMock.gradeVerbForm.mockResolvedValue(gradeResult)
})

describe('VerbSessionScreen', () => {
  it('shows three labelled cards with the open form showing and the translation underneath', async () => {
    const { container } = await render(<VerbSessionScreen step={4} onBack={() => {}} onContinue={() => {}} />)
    await flush()

    expect(states(container)).toEqual(['closed', 'open', 'closed'])
    expect([...container.querySelectorAll('.verb-card-label')].map((l) => l.textContent)).toEqual([
      'V1 · infinitive',
      'V2 · past simple',
      'V3 · past participle',
    ])
    expect(container.querySelectorAll('.verb-card')[1].textContent).toContain('went')
    expect(container.querySelector('.verb-translation')?.textContent).toBe('йти')
    expect(container.querySelector('.verb-progress')?.textContent).toBe('Step 4 · All three forms differ · Verb 1 of 1')
  })

  it('reveals a card on click, grades it with the buttons, and offers Next after both', async () => {
    const { container } = await render(<VerbSessionScreen step={4} onBack={() => {}} onContinue={() => {}} />)
    await flush()

    await click(container.querySelectorAll('.verb-card')[0].querySelector('.verb-card-closed')!)
    expect(states(container)).toEqual(['revealed', 'open', 'closed'])
    expect(container.querySelectorAll('.verb-card')[0].textContent).toContain('go')

    await click(container.querySelector('.verb-got')!)
    expect(apiMock.gradeVerbForm).toHaveBeenCalledWith('go', 'v1', true)
    expect(states(container)).toEqual(['correct', 'open', 'closed'])
    expect(container.querySelector('.verb-next')).toBeNull()

    await click(container.querySelectorAll('.verb-card')[2].querySelector('.verb-card-closed')!)
    await click(container.querySelector('.verb-missed')!)
    expect(apiMock.gradeVerbForm).toHaveBeenCalledWith('go', 'v3', false)
    expect(states(container)).toEqual(['correct', 'open', 'missed'])
    expect(container.querySelector('.verb-next')).not.toBeNull()
  })

  it('works from the keyboard and re-asks only the missed form before the summary', async () => {
    const onContinue = vi.fn()
    const { container } = await render(<VerbSessionScreen step={4} onBack={() => {}} onContinue={onContinue} />)
    await flush()

    await press(' ')
    expect(states(container)).toEqual(['revealed', 'open', 'closed'])
    await press('2')
    expect(states(container)).toEqual(['missed', 'open', 'closed'])
    await press(' ')
    await press('1')
    expect(states(container)).toEqual(['missed', 'open', 'correct'])
    await press('Enter')

    // Second pass: gone stays showing and green, go is closed again.
    expect(states(container)).toEqual(['closed', 'open', 'correct'])
    expect(container.querySelector('.verb-progress')?.textContent).toContain('1 to retry')

    await press(' ')
    await press('1')
    expect(apiMock.gradeVerbForm).toHaveBeenCalledTimes(3)
    await press('Enter')

    expect(container.querySelector('.verb-summary')).not.toBeNull()
    expect(container.querySelector('h1')?.textContent).toBe('1 of 2 forms right first time')
    const retried = container.querySelector('.verb-retried-row')!
    expect(retried.textContent).toContain('go – went – gone')
    expect(retried.querySelector('.is-missed')?.textContent).toBe('go')

    await press('Enter')
    expect(onContinue).toHaveBeenCalledTimes(1)
  })

  it('the summary buttons continue or go back', async () => {
    const onBack = vi.fn()
    const onContinue = vi.fn()
    apiMock.getVerbSession.mockResolvedValue({ ...session, cards: [] })
    const { container } = await render(<VerbSessionScreen step={4} onBack={onBack} onContinue={onContinue} />)
    await flush()

    await click(container.querySelector('.verb-summary .btn-primary')!)
    await click(container.querySelector('.verb-summary .btn-quiet')!)

    expect(onContinue).toHaveBeenCalledTimes(1)
    expect(onBack).toHaveBeenCalledTimes(1)
  })
})
