import { act } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { click, render } from '../test/render'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { parseReaderBook } from './readerBook'
import { ReaderMenu } from './ReaderMenu'
import { DEFAULT_SETTINGS } from './readerSettings'

const book = parseReaderBook(READER_BOOK_XML, 'x')

async function open(dictionaryId: number | null = null) {
  const props = {
    onJump: vi.fn(),
    onSettings: vi.fn(),
    onOpenDictionary: vi.fn(),
    onClose: vi.fn(),
  }
  const view = await render(
    <ReaderMenu
      chapters={book.chapters}
      currentChapter={1}
      settings={DEFAULT_SETTINGS}
      dictionaryId={dictionaryId}
      {...props}
    />,
  )
  return { ...view, ...props }
}

const button = (container: HTMLElement, text: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === text)!

describe('ReaderMenu', () => {
  it('lists the chapters, marks the current one and jumps', async () => {
    const { container, onJump } = await open()

    expect(button(container, 'Year 62').getAttribute('aria-current')).toBe('true')
    await click(button(container, 'The Swordholder'))

    expect(onJump).toHaveBeenCalledWith(0)
  })

  it('changes the theme, the dimming and the text size', async () => {
    const { container, onSettings } = await open()

    await click(button(container, 'Display'))
    await click(button(container, 'Dark'))
    expect(onSettings).toHaveBeenLastCalledWith({ ...DEFAULT_SETTINGS, theme: 'dark' })

    const slider = container.querySelector<HTMLInputElement>('input[type="range"]')!
    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
      setter.call(slider, '40')
      slider.dispatchEvent(new Event('input', { bubbles: true }))
    })
    expect(onSettings).toHaveBeenLastCalledWith({ ...DEFAULT_SETTINGS, dim: 0.4 })

    await click(container.querySelector('[aria-label="Larger text"]')!)
    expect(onSettings).toHaveBeenLastCalledWith({ ...DEFAULT_SETTINGS, textSize: 3 })
  })

  it("links the book's dictionary only when there is one", async () => {
    expect(button((await open()).container, "Open the book's dictionary")).toBeUndefined()

    const { container, onOpenDictionary } = await open(10)
    await click(button(container, "Open the book's dictionary"))

    expect(onOpenDictionary).toHaveBeenCalledWith(10)
  })
})
