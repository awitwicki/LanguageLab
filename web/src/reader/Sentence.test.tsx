import { describe, expect, it, vi } from 'vitest'
import { click, render } from '../test/render'
import { splitSentences } from './readerBook'
import { Sentence, type SentenceTranslation } from './Sentence'
import { toStatusMap } from './wordStatus'

const [sentence] = splitSentences('Most men tried to adjust quickly.')
const statuses = toStatusMap({ learning: ['adjust'], known: ['man'] })

function renderSentence(overrides: { canTranslate?: boolean; translation?: SentenceTranslation } = {}) {
  const onWordTap = vi.fn()
  const onToggleTranslation = vi.fn()

  return render(
    <Sentence
      sentence={sentence}
      positionKey="0.0.0"
      paragraphStart={false}
      statuses={statuses}
      selectedToken={null}
      isBookmark={false}
      canTranslate={overrides.canTranslate ?? true}
      translation={overrides.translation}
      onWordTap={onWordTap}
      onToggleTranslation={onToggleTranslation}
    />,
  ).then((view) => ({ ...view, onWordTap, onToggleTranslation }))
}

const word = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('.reader-word')].find((el) => el.textContent === text)!

describe('Sentence', () => {
  it('underlines new words, colors learning ones and leaves known and function words plain', async () => {
    const { container } = await renderSentence()

    expect(word(container, 'tried').className).toContain('reader-word-new')
    expect(word(container, 'adjust').className).toContain('reader-word-learning')
    expect(word(container, 'men').className).toBe('reader-word')
    expect(word(container, 'to').className).toBe('reader-word')
    expect(container.querySelector('.reader-text')!.textContent).toBe('Most men tried to adjust quickly.')
  })

  it('reports a tapped word with its base form', async () => {
    const { container, onWordTap } = await renderSentence()

    await click(word(container, 'tried'))

    expect(onWordTap).toHaveBeenCalledWith('0.0.0', 4, { lemma: 'try', status: 'new' }, 'tried', expect.any(HTMLElement))
  })

  it('translates the sentence from the strip on the right', async () => {
    const { container, onToggleTranslation } = await renderSentence()
    const strip = container.querySelector<HTMLButtonElement>('.reader-strip')!

    expect(strip.getAttribute('aria-label')).toBe('Translate sentence')
    await click(strip)

    expect(onToggleTranslation).toHaveBeenCalledWith('0.0.0', 'Most men tried to adjust quickly.')
  })

  it('shows an open translation under the sentence', async () => {
    const { container } = await renderSentence({ translation: { state: 'open', text: 'Більшість спробувала.' } })

    expect(container.querySelector('.reader-translation')!.textContent).toBe('Більшість спробувала.')
    expect(container.querySelector('.reader-strip')!.getAttribute('aria-expanded')).toBe('true')
  })

  it('offers a retry after a failure', async () => {
    const { container, onToggleTranslation } = await renderSentence({
      translation: { state: 'error', message: "Couldn't translate" },
    })

    expect(container.querySelector('.reader-translation-error')!.textContent).toContain("Couldn't translate")
    await click(container.querySelector('.reader-translation-error button')!)

    expect(onToggleTranslation).toHaveBeenCalledTimes(1)
  })

  it('has no strip when sentence translation is off', async () => {
    const { container } = await renderSentence({ canTranslate: false })

    expect(container.querySelector('.reader-strip')).toBeNull()
  })
})
