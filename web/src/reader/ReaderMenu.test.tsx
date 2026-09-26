import { act } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { click, render } from '../test/render'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { parseReaderBook } from './readerBook'
import { ReaderMenu } from './ReaderMenu'
import { DEFAULT_SETTINGS, type ReaderSettings } from './readerSettings'

const book = parseReaderBook(READER_BOOK_XML, 'x')

async function open(dictionaryId: number | null = null, settings: ReaderSettings = DEFAULT_SETTINGS) {
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
      settings={settings}
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

  it('turns the whole chapter on and off, showing which it is', async () => {
    const { container, onSettings } = await open()

    await click(button(container, 'Display'))
    const group = container.querySelector('[aria-label="Whole chapter"]')!
    expect(button(group as HTMLElement, 'Off').getAttribute('aria-pressed')).toBe('true')

    await click(button(group as HTMLElement, 'On'))
    expect(onSettings).toHaveBeenLastCalledWith({ ...DEFAULT_SETTINGS, wholeChapter: true })

    const on = await open(null, { ...DEFAULT_SETTINGS, wholeChapter: true })
    await click(button(on.container, 'Display'))
    const onGroup = on.container.querySelector('[aria-label="Whole chapter"]') as HTMLElement
    expect(button(onGroup, 'On').getAttribute('aria-pressed')).toBe('true')

    await click(button(onGroup, 'Off'))
    expect(on.onSettings).toHaveBeenLastCalledWith({ ...DEFAULT_SETTINGS, wholeChapter: false })
  })

  it("links the book's dictionary only when there is one", async () => {
    expect(button((await open()).container, "Open the book's dictionary")).toBeUndefined()

    const { container, onOpenDictionary } = await open(10)
    await click(button(container, "Open the book's dictionary"))

    expect(onOpenDictionary).toHaveBeenCalledWith(10)
  })

  it('closes on Escape', async () => {
    const { onClose } = await open()

    await act(async () => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    })

    expect(onClose).toHaveBeenCalled()
  })
})
