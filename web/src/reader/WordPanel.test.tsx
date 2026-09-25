import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderWord } from '../api/client'
import { click, flush, render } from '../test/render'
import { SHEET_OUT_MS, WordPanel } from './WordPanel'

const apiMock = vi.hoisted(() => ({
  getReaderWord: vi.fn(),
  learnWord: vi.fn(),
  knowWord: vi.fn(),
  ignoreWord: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const adjust: ReaderWord = {
  lemma: 'adjust',
  translation: 'налаштувати',
  source: 'dictionary',
  status: 'new',
  learnTarget: 'book',
}

function setValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

async function open(word: ReaderWord, dictionaryId: number | null = 10) {
  apiMock.getReaderWord.mockResolvedValue(word)
  const onClose = vi.fn()
  const onStatusChange = vi.fn()
  const view = await render(
    <WordPanel
      lemma={word.lemma}
      form="adjusted"
      count={18}
      dictionaryId={dictionaryId}
      onClose={onClose}
      onStatusChange={onStatusChange}
    />,
  )
  await flush()
  return { ...view, onClose, onStatusChange }
}

const closesAfterSlide = async (container: HTMLElement, onClose: () => void) => {
  expect(container.querySelector('.word-panel')!.classList).toContain('word-panel-closing')
  expect(onClose).not.toHaveBeenCalled()
  await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce(), { timeout: SHEET_OUT_MS * 5 })
}

const button = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === text)!

beforeEach(() => {
  apiMock.getReaderWord.mockReset()
  apiMock.learnWord.mockReset().mockResolvedValue(null)
  apiMock.knowWord.mockReset().mockResolvedValue(null)
  apiMock.ignoreWord.mockReset().mockResolvedValue(null)
})

describe('WordPanel', () => {
  it('shows the word, its form, its standing, how often the book uses it and its translation', async () => {
    const { container } = await open(adjust)

    expect(apiMock.getReaderWord).toHaveBeenCalledWith('adjust', 10)
    expect(container.querySelector('.word-panel-lemma')!.textContent).toBe('adjust')
    expect(container.querySelector('.word-panel-form')!.textContent).toBe('adjusted → adjust')
    expect(container.querySelector('.word-panel-meta')!.textContent).toBe('New · seen 18× in this book')
    expect(container.querySelector('.word-panel-translation')!.textContent).toBe('налаштувати')
  })

  it("says where Add to training sends the word", async () => {
    expect((await open(adjust)).container.querySelector('.word-panel-hint')!.textContent).toBe("Goes to this book's words")
    expect((await open({ ...adjust, learnTarget: 'personal' })).container.querySelector('.word-panel-hint')!.textContent).toBe(
      'Goes to My words',
    )
  })

  it('sends the word to training with the book it was read in, then slides away', async () => {
    const { container, onStatusChange, onClose } = await open(adjust)

    await click(button(container, 'Add to training'))
    await flush()

    expect(apiMock.learnWord).toHaveBeenCalledWith('adjust', undefined, 10)
    expect(onStatusChange).toHaveBeenCalledWith('adjust', 'learning')
    expect(container.querySelector('.word-panel-meta')!.textContent).toContain('Learning')
    await closesAfterSlide(container, onClose)
  })

  it('asks for a translation when none was found, and sends the typed one', async () => {
    const { container } = await open({ ...adjust, translation: null, source: 'none', learnTarget: 'personal' }, null)

    expect(container.textContent).toContain('No translation found')
    expect(button(container, 'Add to training').disabled).toBe(true)
    expect(button(container, 'I know it').disabled).toBe(false)
    expect(button(container, 'Ignore').disabled).toBe(false)

    await act(async () => setValue(container.querySelector('input')!, ' налаштувати '))
    await click(button(container, 'Add to training'))

    expect(apiMock.learnWord).toHaveBeenCalledWith('adjust', 'налаштувати', null)
  })

  it('marks the word known, then slides away', async () => {
    const { container, onStatusChange, onClose } = await open(adjust)

    await click(button(container, 'I know it'))
    await flush()

    expect(apiMock.knowWord).toHaveBeenCalledWith('adjust')
    expect(onStatusChange).toHaveBeenCalledWith('adjust', 'known')
    await closesAfterSlide(container, onClose)
  })

  it('ignores a name, stops highlighting it and closes', async () => {
    const { container, onStatusChange, onClose } = await open({ ...adjust, lemma: 'frank' })

    await click(button(container, 'Ignore'))
    await flush()

    expect(apiMock.ignoreWord).toHaveBeenCalledWith('frank')
    expect(onStatusChange).toHaveBeenCalledWith('frank', 'known')
    await closesAfterSlide(container, onClose)
  })

  it('holds the translation place with a shimmering bar while the word is looked up', async () => {
    apiMock.getReaderWord.mockReturnValue(new Promise(() => {}))
    const { container } = await render(
      <WordPanel lemma="adjust" form="adjusted" count={18} dictionaryId={10} onClose={vi.fn()} onStatusChange={vi.fn()} />,
    )

    const placeholder = container.querySelector('.word-panel-skeleton')!

    expect(placeholder.getAttribute('aria-busy')).toBe('true')
    expect(placeholder.querySelectorAll('.skeleton').length).toBe(1)
    expect(container.querySelector('.word-panel-skeleton')).not.toBeNull()
  })

  it('keeps the hint row while looking up, so the buttons stay put when the word lands', async () => {
    apiMock.getReaderWord.mockReturnValue(new Promise(() => {}))
    const { container } = await render(
      <WordPanel lemma="adjust" form="adjusted" count={18} dictionaryId={10} onClose={vi.fn()} onStatusChange={vi.fn()} />,
    )

    expect(container.querySelector('.word-panel-hint .skeleton')).not.toBeNull()
  })

  it('drops the bar once the word is there', async () => {
    const { container } = await open(adjust)

    expect(container.querySelector('.word-panel-skeleton')).toBeNull()
  })

  it('spins on the pressed button alone while its request is in flight', async () => {
    let finish: () => void = () => {}
    apiMock.knowWord.mockReturnValue(
      new Promise<null>((resolve) => {
        finish = () => resolve(null)
      }),
    )
    const { container } = await open(adjust)

    await click(button(container, 'I know it'))

    expect(button(container, 'I know it').getAttribute('aria-busy')).toBe('true')
    expect(button(container, 'I know it').querySelector('.btn-spinner')).not.toBeNull()
    expect(button(container, 'Add to training').getAttribute('aria-busy')).toBe('false')
    expect(button(container, 'Add to training').querySelector('.btn-spinner')).toBeNull()
    expect(button(container, 'Add to training').disabled).toBe(true)

    await act(async () => finish())
    await flush()
  })

  it('says so when the lookup fails', async () => {
    apiMock.getReaderWord.mockRejectedValue(new Error('GET /api/reader/words/adjust → 500'))
    const { container } = await render(
      <WordPanel lemma="adjust" form="adjust" count={1} dictionaryId={null} onClose={vi.fn()} onStatusChange={vi.fn()} />,
    )
    await flush()

    expect(container.querySelector('.word-panel-error')!.textContent).toContain("Couldn't look this word up")
  })

  it('closes on Escape', async () => {
    const { container, onClose } = await open(adjust)

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    })

    await closesAfterSlide(container, onClose)
  })

  it('stays open when the action fails', async () => {
    apiMock.knowWord.mockRejectedValueOnce(new Error('POST /api/reader/words/adjust/known → 500'))
    const { container, onClose } = await open(adjust)

    await click(button(container, 'I know it'))
    await flush()

    expect(container.querySelector('.word-panel-error')).not.toBeNull()
    expect(container.querySelector('.word-panel')!.classList).not.toContain('word-panel-closing')
    expect(onClose).not.toHaveBeenCalled()
  })
})
