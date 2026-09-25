import { describe, expect, it, vi } from 'vitest'
import { click, render } from '../test/render'
import { splitSentences, type ReaderSentence } from './readerBook'
import { Sentence, type SentenceTranslation } from './Sentence'
import { toStatusMap } from './wordStatus'

const [sentence] = splitSentences('Most men tried to adjust quickly.')
const statuses = toStatusMap({ learning: ['adjust'], known: ['man'] })

function renderSentence(
  overrides: { canTranslate?: boolean; translation?: SentenceTranslation; sentence?: ReaderSentence } = {},
) {
  const onWordTap = vi.fn()
  const onToggleTranslation = vi.fn()

  return render(
    <Sentence
      sentence={overrides.sentence ?? sentence}
      positionKey="0.0.0"
      paragraphStart={false}
      statuses={statuses}
      selectedToken={null}
      canTranslate={overrides.canTranslate ?? true}
      translation={overrides.translation}
      onWordTap={onWordTap}
      onToggleTranslation={onToggleTranslation}
    />,
  ).then((view) => ({ ...view, onWordTap, onToggleTranslation }))
}

/** The placeholder's total length in characters — what it claims the translation will take. */
const barChars = (container: HTMLElement) =>
  [...container.querySelectorAll<HTMLElement>('.reader-translation-loading .skeleton')].reduce(
    (total, bar) => total + Number.parseInt(bar.style.width, 10) + 1,
    0,
  )

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

  it('draws the bar as a plain line, coloured while the translation is open', async () => {
    const closed = await renderSentence()
    const strip = closed.container.querySelector('.reader-strip')!

    expect(strip.querySelector('svg')).toBeNull()
    expect(strip.querySelector('.reader-strip-line')).not.toBeNull()
    expect(strip.className).toBe('reader-strip')

    const open = await renderSentence({ translation: { state: 'loading' } })
    expect(open.container.querySelector('.reader-strip')!.className).toBe(
      'reader-strip reader-strip-open reader-strip-loading',
    )
  })

  it('holds the translation place with shimmering bars while it loads', async () => {
    const { container } = await renderSentence({ translation: { state: 'loading' } })
    const placeholder = container.querySelector('.reader-translation-loading')!

    expect(placeholder.getAttribute('role')).toBe('status')
    expect(placeholder.getAttribute('aria-busy')).toBe('true')
    expect(placeholder.querySelectorAll('.skeleton').length).toBeGreaterThan(1)
  })

  it('stands in words as long as the sentence, so the bars wrap where the text will', async () => {
    const [long] = splitSentences(
      'Donald jerked so hard that his mouse went skidding off the pad and across the desk while Mick' +
        ' stood grinning at him from the doorway with his jacket tucked under one arm',
    )
    const short = await renderSentence({ translation: { state: 'loading' } })
    const { container } = await renderSentence({ sentence: long, translation: { state: 'loading' } })

    for (const [view, text] of [
      [short.container, sentence.text],
      [container, long.text],
    ] as const) {
      expect(barChars(view)).toBeGreaterThan(text.length * 0.8)
      expect(barChars(view)).toBeLessThan(text.length * 1.2 + 10)
    }
  })
})
