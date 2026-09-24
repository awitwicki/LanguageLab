import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderWord } from '../api/client'
import { click, flush, render } from '../test/render'
import { WordPanel } from './WordPanel'

const apiMock = vi.hoisted(() => ({
  getReaderWord: vi.fn(),
  learnWord: vi.fn(),
  knowWord: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const adjust: ReaderWord = {
  lemma: 'adjust',
  translation: 'налаштувати',
  source: 'dictionary',
  status: 'new',
  inSharedVocabulary: true,
}

function setValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

async function open(word: ReaderWord, form = 'adjusted') {
  apiMock.getReaderWord.mockResolvedValue(word)
  const onClose = vi.fn()
  const onStatusChange = vi.fn()
  const view = await render(
    <WordPanel lemma={word.lemma} form={form} count={18} onClose={onClose} onStatusChange={onStatusChange} />,
  )
  await flush()
  return { ...view, onClose, onStatusChange }
}

const button = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === text)!

beforeEach(() => {
  apiMock.getReaderWord.mockReset()
  apiMock.learnWord.mockReset().mockResolvedValue(null)
  apiMock.knowWord.mockReset().mockResolvedValue(null)
})

describe('WordPanel', () => {
  it('shows the word, its form in the text, its standing, its count and its translation', async () => {
    const { container } = await open(adjust)

    expect(apiMock.getReaderWord).toHaveBeenCalledWith('adjust')
    expect(container.querySelector('.word-panel-lemma')!.textContent).toBe('adjust')
    expect(container.querySelector('.word-panel-form')!.textContent).toBe('adjusted → adjust')
    expect(container.querySelector('.word-panel-meta')!.textContent).toContain('New')
    expect(container.querySelector('.word-panel-meta')!.textContent).toContain('Count: 18')
    expect(container.querySelector('.word-panel-translation')!.textContent).toBe('налаштувати')
  })

  it('sends the word to training', async () => {
    const { container, onStatusChange } = await open(adjust)

    await click(button(container, 'Learn'))
    await flush()

    expect(apiMock.learnWord).toHaveBeenCalledWith('adjust', undefined)
    expect(onStatusChange).toHaveBeenCalledWith('adjust', 'learning')
    expect(container.querySelector('.word-panel-meta')!.textContent).toContain('Learning')
  })

  it('asks for a translation when none was found, and sends the typed one', async () => {
    const { container } = await open({ ...adjust, translation: null, source: 'none', inSharedVocabulary: false })

    expect(container.textContent).toContain('No translation found')
    expect(button(container, 'Learn').disabled).toBe(true)
    expect(button(container, 'I know it').disabled).toBe(true)

    await act(async () => setValue(container.querySelector('input')!, ' налаштувати '))
    await click(button(container, 'Learn'))

    expect(apiMock.learnWord).toHaveBeenCalledWith('adjust', 'налаштувати')
  })

  it('marks the word known', async () => {
    const { container, onStatusChange } = await open(adjust)

    await click(button(container, 'I know it'))
    await flush()

    expect(apiMock.knowWord).toHaveBeenCalledWith('adjust')
    expect(onStatusChange).toHaveBeenCalledWith('adjust', 'known')
  })

  it('says so when the lookup fails', async () => {
    apiMock.getReaderWord.mockRejectedValue(new Error('GET /api/reader/words/adjust → 500'))
    const { container } = await render(
      <WordPanel lemma="adjust" form="adjust" count={1} onClose={vi.fn()} onStatusChange={vi.fn()} />,
    )
    await flush()

    expect(container.querySelector('.word-panel-error')!.textContent).toContain("Couldn't look this word up")
  })

  it('closes on Escape', async () => {
    const { onClose } = await open(adjust)

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    })

    expect(onClose).toHaveBeenCalled()
  })
})
