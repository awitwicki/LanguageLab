import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { click, flush, render } from '../test/render'
import { LanguagePicker } from './LanguagePicker'

const languages = [
  { code: 'uk', englishName: 'Ukrainian', nativeName: 'Українська' },
  { code: 'pl', englishName: 'Polish', nativeName: 'Polski' },
  { code: 'tl', englishName: 'Filipino', nativeName: 'Filipino' },
]

const apiMock = vi.hoisted(() => ({
  listLanguages: vi.fn(),
  setLanguage: vi.fn(),
}))
vi.mock('../api/client', () => ({ api: apiMock }))

function typeInto(input: HTMLInputElement, value: string) {
  return act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
    setter.call(input, value)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

function codes(container: HTMLElement) {
  return [...container.querySelectorAll('[data-code]')].map((e) => e.getAttribute('data-code'))
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.listLanguages.mockResolvedValue(languages)
})

describe('LanguagePicker', () => {
  it('preselects the suggestion and saves the chosen language', async () => {
    apiMock.setLanguage.mockResolvedValue(null)
    const onSaved = vi.fn()

    const { container } = await render(<LanguagePicker initial="tl" firstRun onSaved={onSaved} />)
    await flush()

    expect(container.querySelector('[aria-checked="true"]')?.textContent).toContain('Filipino')

    await click(container.querySelector('[data-code="pl"]')!)
    await click(container.querySelector('.btn-primary')!)

    expect(apiMock.setLanguage).toHaveBeenCalledWith('pl')
    expect(onSaved).toHaveBeenCalled()
  })

  it('filters by native or English name', async () => {
    const { container } = await render(<LanguagePicker initial="uk" firstRun onSaved={vi.fn()} />)
    await flush()

    const search = container.querySelector('input[type="search"]') as HTMLInputElement
    await typeInto(search, 'pol')
    expect(codes(container)).toEqual(['pl'])

    await typeInto(search, 'укр')
    expect(codes(container)).toEqual(['uk'])
  })

  // First sign-in has nowhere to go back to; the account menu's visit does.
  it('offers Back only outside the first run', async () => {
    const onBack = vi.fn()
    const first = await render(<LanguagePicker initial="uk" firstRun onSaved={vi.fn()} onBack={onBack} />)
    await flush()
    expect(first.container.querySelector('.btn-quiet')).toBeNull()
    expect(first.container.querySelector('.btn-primary')?.textContent).toBe('Continue')

    const later = await render(<LanguagePicker initial="uk" firstRun={false} onSaved={vi.fn()} onBack={onBack} />)
    await flush()
    expect(later.container.querySelector('.btn-primary')?.textContent).toBe('Save')
    await click(later.container.querySelector('.btn-quiet')!)
    expect(onBack).toHaveBeenCalled()
  })

  it('keeps the picker open and shows the reason when saving fails', async () => {
    apiMock.setLanguage.mockRejectedValue(new Error('Unknown language.'))
    const onSaved = vi.fn()
    const { container } = await render(<LanguagePicker initial="uk" firstRun onSaved={onSaved} />)
    await flush()

    await click(container.querySelector('.btn-primary')!)
    await flush()

    expect(onSaved).not.toHaveBeenCalled()
    expect(container.querySelector('.error')?.textContent).toBe('Unknown language.')
    expect(container.querySelector<HTMLButtonElement>('.btn-primary')!.disabled).toBe(false)
  })
})
