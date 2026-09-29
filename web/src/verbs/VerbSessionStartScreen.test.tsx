import { describe, expect, it, vi } from 'vitest'
import type { SessionVerb } from '../api/client'
import { click, render } from '../test/render'
import { VerbSessionStartScreen } from './VerbSessionStartScreen'

function verb(v1: string): SessionVerb {
  return {
    v1,
    v2: `${v1}-2`,
    v3: `${v1}-3`,
    translation: `переклад ${v1}`,
    group: 4,
    note: null,
    exercises: [],
    formOrder: ['v2', 'v1', 'v3'],
    mastery: 0,
    streak: 0,
    answers: 0,
  }
}

function screen(offer: SessionVerb[], words: number, handlers = {}) {
  return (
    <VerbSessionStartScreen offer={offer} words={words} onWords={() => {}} onStart={() => {}} {...handlers} />
  )
}

const five = ['go', 'see', 'take', 'give', 'make'].map(verb)

describe('VerbSessionStartScreen', () => {
  it('lists the words on offer', async () => {
    const { container } = await render(screen(five, 5))

    expect(container.querySelector('.start-words')!.textContent).toContain('go')
    expect(container.querySelector('.start-words')!.textContent).toContain('make')
  })

  it('lists only the words the session will take', async () => {
    const { container } = await render(screen(five, 2))

    const text = container.querySelector('.start-words')!.textContent!
    expect(text).toContain('go')
    expect(text).not.toContain('make')
  })

  it('steps the words up and down', async () => {
    const onWords = vi.fn()
    const { container } = await render(screen(five, 3, { onWords }))

    await click(container.querySelector('.start-words-more')!)
    expect(onWords).toHaveBeenCalledWith(4)

    await click(container.querySelector('.start-words-less')!)
    expect(onWords).toHaveBeenCalledWith(2)
  })

  /// Review focus 2: a nearly finished stage has fewer than five words left to offer.
  it('cannot ask for more words than the window holds', async () => {
    const onWords = vi.fn()
    const two = five.slice(0, 2)
    const { container } = await render(screen(two, 2, { onWords }))

    expect(container.querySelector<HTMLButtonElement>('.start-words-more')!.disabled).toBe(true)

    await click(container.querySelector('.start-words-more')!)
    expect(onWords).not.toHaveBeenCalled()
  })

  it('cannot ask for fewer than one word', async () => {
    const onWords = vi.fn()
    const { container } = await render(screen(five, 1, { onWords }))

    expect(container.querySelector<HTMLButtonElement>('.start-words-less')!.disabled).toBe(true)

    await click(container.querySelector('.start-words-less')!)
    expect(onWords).not.toHaveBeenCalled()
  })

  it('starts training', async () => {
    const onStart = vi.fn()
    const { container } = await render(screen(five, 5, { onStart }))

    await click(container.querySelector('.start-train')!)
    expect(onStart).toHaveBeenCalledTimes(1)
  })
})
