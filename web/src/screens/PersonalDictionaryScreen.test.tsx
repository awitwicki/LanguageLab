import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { PersonalDictionary, PersonalWord, TrainingStarted } from '../api/client'
import { click, flush, render } from '../test/render'
import { PersonalDictionaryScreen } from './PersonalDictionaryScreen'

const apiMock = vi.hoisted(() => ({
  getPersonalDictionary: vi.fn(),
  translate: vi.fn(),
  addPersonalWord: vi.fn(),
  removePersonalWord: vi.fn(),
  startReview: vi.fn(),
  importPersonalWords: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const empty: PersonalDictionary = {
  id: 5,
  name: 'My words',
  wordsCount: 0,
  learnableCount: 0,
  dueCount: 0,
  learning: { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 },
  words: [],
}
const apple: PersonalWord = { wordPairId: 1, word: 'apple', translation: 'яблуко', box: null, isLearned: false }
const run: PersonalWord = { wordPairId: 2, word: 'run', translation: 'бігти', box: 2, isLearned: false }
const withWords: PersonalDictionary = {
  ...empty,
  wordsCount: 2,
  learnableCount: 1,
  dueCount: 1,
  learning: { notStarted: 1, boxes: [0, 1, 0, 0, 0], learned: 0, total: 2 },
  words: [run, apple],
}
const reviewStarted: TrainingStarted = { trainingId: 9, mode: 'review', words: [], totalQuestions: 4 }

function setValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

function type(input: HTMLInputElement, value: string) {
  return act(async () => setValue(input, value))
}

function setTextAreaValue(el: HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value')!.set!
  setter.call(el, value)
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

function typeArea(el: HTMLTextAreaElement, value: string) {
  return act(async () => setTextAreaValue(el, value))
}

function pressEnter(input: HTMLInputElement) {
  return act(async () => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
  })
}

function submit(form: HTMLFormElement) {
  return act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  })
}

function screen(overrides: Partial<Parameters<typeof PersonalDictionaryScreen>[0]> = {}) {
  return <PersonalDictionaryScreen onTrain={() => {}} onReview={() => {}} {...overrides} />
}

function fields(container: HTMLElement) {
  return {
    word: container.querySelector<HTMLInputElement>('input[name="word"]')!,
    translation: container.querySelector<HTMLInputElement>('input[name="translation"]')!,
    form: container.querySelector<HTMLFormElement>('form.add-word')!,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.getPersonalDictionary.mockResolvedValue(empty)
})

describe('PersonalDictionaryScreen', () => {
  it('Enter in the word field looks the word up and fills the translation', async () => {
    apiMock.translate.mockResolvedValue({ word: 'apple', translation: 'яблуко', source: 'myMemory' })
    const { container } = await render(screen())
    await flush()
    const { word, translation } = fields(container)

    await type(word, 'apple')
    await pressEnter(word)
    await flush()

    expect(apiMock.translate).toHaveBeenCalledWith('apple')
    expect(translation.value).toBe('яблуко')
    expect(apiMock.addPersonalWord).not.toHaveBeenCalled()
  })

  it('a hand-typed translation survives a later lookup', async () => {
    apiMock.translate.mockResolvedValue({ word: 'apple', translation: 'яблуко', source: 'myMemory' })
    const { container } = await render(screen())
    await flush()
    const { word, translation } = fields(container)

    await type(translation, 'моє слово')
    await type(word, 'apple')
    await pressEnter(word)
    await flush()

    expect(translation.value).toBe('моє слово')
  })

  it('a suggestion is replaced by the next suggestion', async () => {
    apiMock.translate
      .mockResolvedValueOnce({ word: 'apple', translation: 'яблуко', source: 'myMemory' })
      .mockResolvedValueOnce({ word: 'pear', translation: 'груша', source: 'dictionary' })
    const { container } = await render(screen())
    await flush()
    const { word, translation } = fields(container)

    await type(word, 'apple')
    await pressEnter(word)
    await flush()
    await type(word, 'pear')
    await pressEnter(word)
    await flush()

    expect(translation.value).toBe('груша')
  })

  it('says so when nothing was found', async () => {
    apiMock.translate.mockResolvedValue({ word: 'zzz', translation: null, source: 'none' })
    const { container } = await render(screen())
    await flush()
    const { word, translation } = fields(container)

    await type(word, 'zzz')
    await pressEnter(word)
    await flush()

    expect(container.textContent).toContain('Nothing found')
    expect(translation.value).toBe('')
  })

  it('submitting adds the word, clears the form, reloads the list and reports the change', async () => {
    apiMock.addPersonalWord.mockResolvedValue(apple)
    apiMock.getPersonalDictionary.mockResolvedValueOnce(empty).mockResolvedValueOnce({ ...withWords, words: [apple] })
    const onChanged = vi.fn()
    const { container } = await render(screen({ onChanged }))
    await flush()
    const { word, translation, form } = fields(container)

    await type(word, 'apple')
    await type(translation, 'яблуко')
    await submit(form)
    await flush()

    expect(apiMock.addPersonalWord).toHaveBeenCalledWith('apple', 'яблуко')
    expect(word.value).toBe('')
    expect(translation.value).toBe('')
    expect(onChanged).toHaveBeenCalledTimes(1)

    const row = container.querySelector('.personal-word')!
    expect(row.querySelector('.word')?.textContent).toBe('apple')
    expect(row.querySelector('.state')?.textContent).toBe('New')
    expect(document.activeElement).toBe(word)
  })

  it('shows the server message on a conflict', async () => {
    apiMock.addPersonalWord.mockRejectedValue(new Error('Already in your dictionary.'))
    const { container } = await render(screen())
    await flush()
    const { word, translation, form } = fields(container)

    await type(word, 'apple')
    await type(translation, 'яблуко')
    await submit(form)
    await flush()

    expect(container.textContent).toContain('Already in your dictionary.')
    expect(word.value).toBe('apple')
  })

  it('Add stays disabled until both fields are filled', async () => {
    const { container } = await render(screen())
    await flush()
    const { word, translation } = fields(container)
    const add = container.querySelector<HTMLButtonElement>('form.add-word button[type="submit"]')!

    expect(add.disabled).toBe(true)
    await type(word, 'apple')
    expect(add.disabled).toBe(true)
    await type(translation, 'яблуко')
    expect(add.disabled).toBe(false)
  })

  it('lists words newest first with their state and removes one on click', async () => {
    apiMock.getPersonalDictionary.mockResolvedValueOnce(withWords).mockResolvedValueOnce({ ...withWords, words: [apple] })
    apiMock.removePersonalWord.mockResolvedValue(null)
    const onChanged = vi.fn()
    const { container } = await render(screen({ onChanged }))
    await flush()

    const rows = [...container.querySelectorAll('.personal-word')]
    expect(rows.map((r) => r.querySelector('.word')?.textContent)).toEqual(['run', 'apple'])
    expect(rows.map((r) => r.querySelector('.state')?.textContent)).toEqual(['Box 2', 'New'])

    await click(container.querySelector('button[aria-label="Remove run"]')!)
    await flush()

    expect(apiMock.removePersonalWord).toHaveBeenCalledWith(2)
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect([...container.querySelectorAll('.personal-word .word')].map((w) => w.textContent)).toEqual(['apple'])
  })

  it('Start exercise is disabled with nothing to learn and otherwise opens the scope', async () => {
    const onTrain = vi.fn()
    const { container, rerender } = await render(screen({ onTrain }))
    await flush()

    const button = () => [...container.querySelectorAll<HTMLButtonElement>('.btn')].find((b) => b.textContent === 'Start exercise')!

    expect(button().disabled).toBe(true)
    expect(container.textContent).toContain('Add a word to start')

    apiMock.getPersonalDictionary.mockResolvedValue(withWords)
    await rerender(<PersonalDictionaryScreen key="second" onTrain={onTrain} onReview={() => {}} />)
    await flush()

    expect(button().disabled).toBe(false)
    await click(button())
    expect(onTrain).toHaveBeenCalledWith(5)
  })

  it('Review starts a review scoped to the personal dictionary', async () => {
    apiMock.getPersonalDictionary.mockResolvedValue(withWords)
    apiMock.startReview.mockResolvedValue(reviewStarted)
    const onReview = vi.fn()
    const { container } = await render(screen({ onReview }))
    await flush()

    const review = [...container.querySelectorAll<HTMLButtonElement>('.btn')].find((b) => b.textContent?.startsWith('Review'))!
    expect(review.textContent).toBe('Review (1)')

    await click(review)
    await flush()

    expect(apiMock.startReview).toHaveBeenCalledWith({ dictionaryId: 5, chapterIds: null })
    expect(onReview).toHaveBeenCalledWith(reviewStarted, 5)
  })

  it('Import multiple words toggles a textarea for pasting lines', async () => {
    const { container } = await render(screen())
    await flush()

    const toggle = () => container.querySelector<HTMLButtonElement>('.bulk-import > button')!

    expect(container.querySelector('textarea[name="bulk"]')).toBeNull()

    await click(toggle())
    expect(container.querySelector('textarea[name="bulk"]')).not.toBeNull()

    await click(toggle())
    expect(container.querySelector('textarea[name="bulk"]')).toBeNull()
  })

  it('imports parsed lines, shows a summary, reloads the list and reports the change', async () => {
    apiMock.importPersonalWords.mockResolvedValue([
      { word: 'banana', translation: 'банан', added: true, error: null },
      { word: 'apple', translation: 'яблуко', added: false, error: 'Already in your dictionary.' },
    ])
    apiMock.getPersonalDictionary.mockResolvedValueOnce(empty).mockResolvedValueOnce({ ...withWords, words: [apple] })
    const onChanged = vi.fn()
    const { container } = await render(screen({ onChanged }))
    await flush()

    await click(
      [...container.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === 'Import multiple words')!,
    )
    const textarea = container.querySelector<HTMLTextAreaElement>('textarea[name="bulk"]')!
    await typeArea(textarea, 'banana,банан\napple,яблуко')
    await click(container.querySelector<HTMLButtonElement>('form.bulk-import-form button[type="submit"]')!)
    await flush()

    expect(apiMock.importPersonalWords).toHaveBeenCalledWith([
      { word: 'banana', translation: 'банан' },
      { word: 'apple', translation: 'яблуко' },
    ])
    expect(container.textContent).toContain('1 added')
    expect(container.textContent).toContain('apple')
    expect(container.textContent).toContain('Already in your dictionary.')
    expect(textarea.value).toBe('')
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(apiMock.getPersonalDictionary).toHaveBeenCalledTimes(2)
  })

  it('a line without a comma is reported as invalid without being sent to the server', async () => {
    apiMock.importPersonalWords.mockResolvedValue([{ word: 'mango', translation: 'манго', added: true, error: null }])
    const { container } = await render(screen())
    await flush()

    await click(
      [...container.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent === 'Import multiple words')!,
    )
    const textarea = container.querySelector<HTMLTextAreaElement>('textarea[name="bulk"]')!
    await typeArea(textarea, 'bad-line\nmango,манго')
    await click(container.querySelector<HTMLButtonElement>('form.bulk-import-form button[type="submit"]')!)
    await flush()

    expect(apiMock.importPersonalWords).toHaveBeenCalledWith([{ word: 'mango', translation: 'манго' }])
    expect(container.textContent).toContain('bad-line')
    expect(container.textContent).toContain('1 added')
  })

  it('a learned word reads Learned and the scale appears once anything was trained', async () => {
    apiMock.getPersonalDictionary.mockResolvedValue({
      ...withWords,
      words: [{ ...run, box: 5, isLearned: true }],
    })
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.personal-word .state')?.textContent).toBe('Learned')
    expect(container.querySelector('.personal-learning')).not.toBeNull()
  })
})
