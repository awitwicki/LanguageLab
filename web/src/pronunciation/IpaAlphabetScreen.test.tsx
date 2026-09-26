import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { IpaAlphabet, IpaEntry } from '../api/client'
import { click, flush, render } from '../test/render'
import { IpaAlphabetScreen } from './IpaAlphabetScreen'

const apiMock = vi.hoisted(() => ({ getIpaAlphabet: vi.fn() }))
const supportMock = vi.hoisted(() => ({ isSpeechRecognitionSupported: vi.fn(() => true) }))

vi.mock('../api/client', () => ({ api: apiMock }))
vi.mock('./speechRecognition', () => supportMock)

function entry(over: Partial<IpaEntry> = {}): IpaEntry {
  return {
    symbol: 'ʃ',
    name: 'voiceless postalveolar fricative',
    hint: 'sh as in "ship"',
    group: 'Fricative',
    inEnglish: true,
    exampleWord: 'ship',
    exampleLanguage: 'English',
    exampleIpa: '/ʃɪp/',
    soundAudio: '/pronunciation-audio/ipa-voiceless-postalveolar-fricative.ogg',
    wordAudio: '/pronunciation-audio/ship-en.ogg',
    familyKey: null,
    aliases: [],
    ...over,
  }
}

const ALPHABET: IpaAlphabet = {
  sections: [
    {
      key: 'pulmonic',
      title: 'Pulmonic consonants',
      note: 'Made with air from the lungs.',
      entries: [
        entry(),
        entry({
          symbol: 'θ',
          name: 'voiceless dental fricative',
          hint: 'th as in "think"',
          exampleWord: 'think',
          exampleIpa: '/ˈθɪŋk/',
          familyKey: 'th-sounds',
          wordAudio: '/pronunciation-audio/think-us.ogg',
        }),
        entry({
          symbol: 'r',
          name: 'alveolar trill',
          hint: 'a rolled r, as Ukrainian р',
          group: 'Trill',
          inEnglish: false,
          exampleWord: 'рука',
          exampleLanguage: 'Ukrainian',
          exampleIpa: '/rʊˈka/',
          wordAudio: null,
        }),
      ],
    },
    {
      key: 'non-pulmonic',
      title: 'Non-pulmonic consonants',
      note: 'Made without lung air.',
      entries: [
        entry({
          symbol: 'ʘ',
          name: 'bilabial click',
          hint: 'a lip smack',
          group: 'Click',
          inEnglish: false,
          exampleWord: null,
          exampleLanguage: null,
          exampleIpa: null,
          soundAudio: null,
          wordAudio: null,
        }),
      ],
    },
  ],
}

function search(container: HTMLElement) {
  return container.querySelector<HTMLInputElement>('input[name="symbol"]')!
}

function type(input: HTMLInputElement, value: string) {
  return act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
    setter.call(input, value)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

function toggleEnglishOnly(container: HTMLElement) {
  const box = container.querySelector<HTMLInputElement>('.ipa-toggle input')!
  return act(async () => {
    box.click()
  })
}

function symbols(container: HTMLElement) {
  return [...container.querySelectorAll('.ipa-symbol')].map((el) => el.textContent)
}

beforeEach(() => {
  apiMock.getIpaAlphabet.mockReset().mockResolvedValue(ALPHABET)
  supportMock.isSpeechRecognitionSupported.mockReset().mockReturnValue(true)
})

describe('IpaAlphabetScreen', () => {
  it('shows every symbol of every section, with its name, hint and example', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    expect(symbols(container)).toEqual(['ʃ', 'θ', 'r', 'ʘ'])
    expect(container.textContent).toContain('Pulmonic consonants')
    expect(container.textContent).toContain('Non-pulmonic consonants')
    expect(container.textContent).toContain('voiceless postalveolar fricative')
    expect(container.textContent).toContain('sh as in "ship"')
    expect(container.textContent).toContain('/ʃɪp/')
    expect(container.textContent).toContain('4 symbols')
  })

  it('names the language of a foreign example, and leaves an English one unlabelled', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    const languages = [...container.querySelectorAll('.ipa-example-language')].map((el) => el.textContent)
    expect(languages).toEqual(['Ukrainian'])
  })

  it('heads the chart rows of a section that holds more than one', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    const groups = [...container.querySelectorAll('.ipa-group-title')].map((el) => el.textContent)
    // Pulmonic holds Fricative and Trill; non-pulmonic holds only Click, so it gets no heading.
    expect(groups).toEqual(['Fricative', 'Trill'])
  })

  it('filters by a symbol pasted into the search box', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    await type(search(container), 'θ')

    expect(symbols(container)).toEqual(['θ'])
    expect(container.textContent).toContain('1 of 4 symbols')
  })

  it('filters by the name of a sound, dropping the sections it empties', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    await type(search(container), 'trill')

    expect(symbols(container)).toEqual(['r'])
    expect(container.textContent).not.toContain('Non-pulmonic consonants')
  })

  it('says so when a search matches nothing', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    await type(search(container), 'zzz')

    expect(symbols(container)).toEqual([])
    expect(container.textContent).toContain('Nothing matches')
  })

  it('narrows the chart to the sounds English uses', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    await toggleEnglishOnly(container)

    expect(symbols(container)).toEqual(['ʃ', 'θ'])
  })

  it('plays the sound clip and the example word from the same element', async () => {
    const play = vi
      .spyOn(HTMLMediaElement.prototype, 'play')
      .mockImplementation(() => Promise.resolve())
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    const card = container.querySelector('.ipa-card')!
    const buttons = [...card.querySelectorAll('button')]
    await click(buttons.find((b) => b.textContent === 'Play the sound')!)

    const audio = card.querySelector('audio')!
    expect(audio.src).toContain('ipa-voiceless-postalveolar-fricative.ogg')

    await click(buttons.find((b) => b.textContent?.includes('ship'))!)
    expect(audio.src).toContain('ship-en.ogg')
    expect(play).toHaveBeenCalledTimes(2)

    play.mockRestore()
  })

  it('says a symbol has no recording rather than showing a dead button', async () => {
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    const clickCard = [...container.querySelectorAll('.ipa-card')].find((card) =>
      card.textContent?.includes('bilabial click'),
    )!
    expect(clickCard.querySelectorAll('button')).toHaveLength(0)
    expect(clickCard.textContent).toContain('No recording yet')
  })

  it('leads into the trainer family that drills a sound', async () => {
    const onOpenFamily = vi.fn()
    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={onOpenFamily} />)
    await flush()

    const practice = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Practice it')!
    await click(practice)

    expect(onOpenFamily).toHaveBeenCalledWith('th-sounds')
  })

  it('offers no practice link where speech recognition cannot run, but still shows the chart', async () => {
    supportMock.isSpeechRecognitionSupported.mockReturnValue(false)

    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    expect(symbols(container)).toEqual(['ʃ', 'θ', 'r', 'ʘ'])
    expect(container.textContent).not.toContain('Practice it')
  })

  it('goes back to the mode it came from', async () => {
    const onBack = vi.fn()
    const { container } = await render(<IpaAlphabetScreen onBack={onBack} onOpenFamily={() => {}} />)
    await flush()

    await click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Back')!)

    expect(onBack).toHaveBeenCalled()
  })

  it('reports a failed load instead of an empty chart', async () => {
    apiMock.getIpaAlphabet.mockRejectedValue(new Error('nope'))

    const { container } = await render(<IpaAlphabetScreen onBack={() => {}} onOpenFamily={() => {}} />)
    await flush()

    expect(container.querySelector('.error')?.textContent).toContain('nope')
  })
})
