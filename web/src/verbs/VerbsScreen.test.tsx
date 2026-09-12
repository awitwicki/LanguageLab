import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { FormProgressView, FormState, IrregularVerbsOverview, VerbView } from '../api/client'
import { click, flush, render } from '../test/render'
import { VerbsScreen } from './VerbsScreen'

const apiMock = vi.hoisted(() => ({ getIrregularVerbs: vi.fn() }))
vi.mock('../api/client', () => ({ api: apiMock }))

type Marks = [FormState, number][]

function forms(marks: Marks): FormProgressView[] {
  return (['v1', 'v2', 'v3'] as const).map((form, i) => ({
    form,
    state: marks[i][0],
    streak: marks[i][1],
    correct: 0,
    wrong: 0,
  }))
}

function verb(v1: string, v2: string, v3: string, translation: string, marks: Marks): VerbView {
  const f = forms(marks)
  return { v1, v2, v3, translation, learned: f.every((x) => x.state === 'learned'), forms: f }
}

const unseen: Marks = [['unseen', 0], ['unseen', 0], ['unseen', 0]]
const learned: Marks = [['learned', 3], ['learned', 4], ['learned', 3]]

const overview: IrregularVerbsOverview = {
  steps: [
    {
      step: 1, title: 'All three forms alike', totalVerbs: 2, learnedVerbs: 0, totalForms: 6, learnedForms: 0,
      verbs: [verb('cut', 'cut', 'cut', 'різати', unseen), verb('put', 'put', 'put', 'класти', unseen)],
    },
    {
      step: 2, title: 'First and third alike', totalVerbs: 1, learnedVerbs: 1, totalForms: 3, learnedForms: 3,
      verbs: [verb('run', 'ran', 'run', 'бігти', learned)],
    },
    {
      step: 3, title: 'Second and third alike', totalVerbs: 1, learnedVerbs: 0, totalForms: 3, learnedForms: 1,
      verbs: [verb('buy', 'bought', 'bought', 'купувати', [['unseen', 0], ['missed', 0], ['learned', 3]])],
    },
    {
      step: 4, title: 'All three forms differ', totalVerbs: 1, learnedVerbs: 0, totalForms: 3, learnedForms: 0,
      verbs: [verb('go', 'went', 'gone', 'йти', [['unseen', 0], ['learning', 2], ['unseen', 0]])],
    },
  ],
}

beforeEach(() => {
  apiMock.getIrregularVerbs.mockReset()
  apiMock.getIrregularVerbs.mockResolvedValue(overview)
})

describe('VerbsScreen', () => {
  it('labels each step Start, Continue or Review by its progress', async () => {
    const { container } = await render(<VerbsScreen onStartSession={() => {}} />)
    await flush()

    const labels = [...container.querySelectorAll('.step-action')].map((b) => b.textContent)
    expect(labels).toEqual(['Start', 'Review', 'Continue', 'Continue'])
  })

  it('shows learned forms on the bar and learned verbs in the caption', async () => {
    const { container } = await render(<VerbsScreen onStartSession={() => {}} />)
    await flush()

    const cards = [...container.querySelectorAll('.step-card')]
    expect(cards[2].querySelector('.progress-track')?.getAttribute('aria-valuenow')).toBe('33')
    expect(cards[2].querySelector('.step-caption')?.textContent).toBe('0 of 1 verbs learned')
    expect(cards[1].querySelector('.step-caption')?.textContent).toBe('1 of 1 verbs learned')
  })

  it('expands a step table with a state and streak dots per form, plus a legend', async () => {
    const { container } = await render(<VerbsScreen onStartSession={() => {}} />)
    await flush()

    expect(container.querySelector('.step-table')).toBeNull()

    await click(container.querySelectorAll('.step-table-toggle')[2])

    const row = container.querySelector('.step-table tbody tr')!
    const cells = [...row.querySelectorAll('.verb-cell')]
    expect(cells.map((c) => c.getAttribute('data-state'))).toEqual(['unseen', 'missed', 'learned'])
    expect(cells.map((c) => c.querySelector('.verb-cell-form')?.textContent)).toEqual(['buy', 'bought', 'bought'])
    expect(cells.map((c) => c.querySelectorAll('.verb-dot.is-on').length)).toEqual([0, 0, 3])
    expect(row.querySelector('.verb-cell-translation')?.textContent).toBe('купувати')
    expect(container.querySelector('.verb-legend')?.textContent).toContain('not seen')
  })

  it('starts a session for the clicked step', async () => {
    const onStartSession = vi.fn()
    const { container } = await render(<VerbsScreen onStartSession={onStartSession} />)
    await flush()

    await click(container.querySelectorAll('.step-action')[3])

    expect(onStartSession).toHaveBeenCalledWith(4)
  })
})
