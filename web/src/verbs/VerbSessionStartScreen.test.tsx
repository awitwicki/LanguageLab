import { describe, expect, it, vi } from 'vitest'
import type { SessionVerb } from '../api/client'
import { click, render } from '../test/render'
import { MaxRounds } from './introSettings'
import { VerbSessionStartScreen } from './VerbSessionStartScreen'

function verb(v1: string): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 4,
    note: null,
    examples: { present: `I [${v1}].`, past: `I [${v1}-2].`, perfect: `I have [${v1}-3].` },
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
    fresh: true,
  }
}

function screen(offer: SessionVerb[], words: number, rounds: number, handlers = {}) {
  return (
    <VerbSessionStartScreen
      offer={offer}
      settings={{ words, rounds }}
      onWords={() => {}}
      onRounds={() => {}}
      onStart={() => {}}
      onSkip={() => {}}
      {...handlers}
    />
  )
}

const five = ['go', 'see', 'take', 'give', 'make'].map(verb)

describe('VerbSessionStartScreen', () => {
  it('lists the words on offer and what the introduction adds up to', async () => {
    const { container } = await render(screen(five, 5, 3))

    expect(container.querySelector('.start-words')!.textContent).toContain('go')
    expect(container.querySelector('.start-words')!.textContent).toContain('make')
    expect(container.querySelector('.start-summary')!.textContent).toBe('5 words × 3 rounds = 15 cards')
  })

  it('counts only the words the session will take', async () => {
    const { container } = await render(screen(five, 2, 3))

    expect(container.querySelector('.start-summary')!.textContent).toBe('2 words × 3 rounds = 6 cards')
  })

  it('says it in the singular for one word and one round', async () => {
    const { container } = await render(screen(five, 1, 1))

    expect(container.querySelector('.start-summary')!.textContent).toBe('1 word × 1 round = 1 card')
  })

  it('steps the words up and down', async () => {
    const onWords = vi.fn()
    const { container } = await render(screen(five, 3, 3, { onWords }))

    await click(container.querySelector('.start-words-more')!)
    expect(onWords).toHaveBeenCalledWith(4)

    await click(container.querySelector('.start-words-less')!)
    expect(onWords).toHaveBeenCalledWith(2)
  })

  /// Review focus 2: a nearly finished stage has fewer than five words left to offer.
  it('cannot ask for more words than the window holds', async () => {
    const onWords = vi.fn()
    const two = five.slice(0, 2)
    const { container } = await render(screen(two, 2, 3, { onWords }))

    expect(container.querySelector('.start-summary')!.textContent).toBe('2 words × 3 rounds = 6 cards')
    expect(container.querySelector<HTMLButtonElement>('.start-words-more')!.disabled).toBe(true)

    await click(container.querySelector('.start-words-more')!)
    expect(onWords).not.toHaveBeenCalled()
  })

  it('cannot ask for fewer than one word or more rounds than the cap', async () => {
    const onWords = vi.fn()
    const onRounds = vi.fn()
    const { container } = await render(screen(five, 1, MaxRounds, { onWords, onRounds }))

    expect(container.querySelector<HTMLButtonElement>('.start-words-less')!.disabled).toBe(true)
    expect(container.querySelector<HTMLButtonElement>('.start-rounds-more')!.disabled).toBe(true)

    await click(container.querySelector('.start-words-less')!)
    await click(container.querySelector('.start-rounds-more')!)

    expect(onWords).not.toHaveBeenCalled()
    expect(onRounds).not.toHaveBeenCalled()
  })

  it('starts the introduction or goes straight to training', async () => {
    const onStart = vi.fn()
    const onSkip = vi.fn()
    const { container } = await render(screen(five, 5, 3, { onStart, onSkip }))

    await click(container.querySelector('.start-intro')!)
    expect(onStart).toHaveBeenCalled()

    await click(container.querySelector('.start-skip')!)
    expect(onSkip).toHaveBeenCalled()
  })
})
