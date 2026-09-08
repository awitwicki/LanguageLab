import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { NextQuestion, TrainingStarted, TrainingSummary } from '../api/client'
import { click, flush, render } from '../test/render'
import { TrainingScreen } from './TrainingScreen'

const apiMock = vi.hoisted(() => ({
  nextQuestion: vi.fn(),
  answer: vi.fn(),
  markKnown: vi.fn(),
  finish: vi.fn(),
  retry: vi.fn(),
  startNewBatch: vi.fn(),
}))

vi.mock('../api/client', () => ({ api: apiMock }))

const started: TrainingStarted = {
  trainingId: 5,
  mode: 'newBatch',
  words: [
    { wordPairId: 1, word: 'abide', translation: 'дотримуватися' },
    { wordPairId: 2, word: 'silo', translation: 'силос' },
  ],
  totalQuestions: 4,
}

const questionOne: NextQuestion = {
  question: {
    id: 100,
    wordPairId: 1,
    direction: 'enToUa',
    prompt: 'abide',
    options: [
      { wordPairId: 2, label: 'силос' },
      { wordPairId: 1, label: 'дотримуватися' },
      { wordPairId: 3, label: 'зненацька' },
    ],
  },
  answered: 0,
  total: 4,
}

const questionTwo: NextQuestion = {
  question: { ...questionOne.question!, id: 101, wordPairId: 2, prompt: 'silo' },
  answered: 1,
  total: 4,
}

const exhausted: NextQuestion = { question: null, answered: 4, total: 4 }

const summary: TrainingSummary = {
  correct: 3,
  total: 4,
  ratio: 0.75,
  passed: false,
  words: [
    { word: 'silo', translation: 'силос', correct: 2, total: 2, box: 2, dueAt: '2026-09-10T12:00:00Z', isLearned: false },
    { word: 'abide', translation: 'дотримуватися', correct: 1, total: 2, box: 1, dueAt: '2026-09-08T12:00:00Z', isLearned: false },
  ],
}

function screen(overrides: Partial<Parameters<typeof TrainingScreen>[0]> = {}) {
  return (
    <TrainingScreen
      dictionaryId={7}
      dictionaryName="Wool"
      scopeTitle="Holston"
      chapterIds={[11]}
      batchSize={10}
      started={started}
      onBack={() => {}}
      {...overrides}
    />
  )
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

function press(key: string) {
  return act(async () => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.nextQuestion.mockResolvedValue(questionOne)
  apiMock.answer.mockResolvedValue({ isCorrect: false, correctWordPairId: 1, word: 'abide', translation: 'дотримуватися' })
  apiMock.markKnown.mockResolvedValue({ word: 'abide' })
  apiMock.finish.mockResolvedValue(summary)
})

describe('TrainingScreen — картки', () => {
  it('новий батч починається з карток; «Почати квіз» тягне перше питання', async () => {
    const { container } = await render(screen())

    expect(container.textContent).toContain('Нові слова')
    expect(container.textContent).toContain('abide')
    expect(container.textContent).toContain('дотримуватися')
    expect(apiMock.nextQuestion).not.toHaveBeenCalled()

    await click(buttons(container).find((b) => b.textContent?.includes('Почати квіз'))!)
    await flush()

    expect(apiMock.nextQuestion).toHaveBeenCalledWith(5)
    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('abide')
  })

  it('Enter на картках запускає квіз', async () => {
    const { container } = await render(screen())

    await press('Enter')
    await flush()

    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('abide')
  })

  it('повторення пропускає картки', async () => {
    const { container } = await render(screen({ started: { ...started, mode: 'review' }, batchSize: null }))
    await flush()

    expect(container.textContent).not.toContain('Нові слова')
    expect(apiMock.nextQuestion).toHaveBeenCalledWith(5)
    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('abide')
  })
})

describe('TrainingScreen — квіз', () => {
  async function openQuiz() {
    const result = await render(screen({ started: { ...started, mode: 'review' }, batchSize: null }))
    await flush()
    return result
  }

  it('варіанти з номерами; лічильник «Питання 1 з 4»', async () => {
    const { container } = await openQuiz()

    const options = [...container.querySelectorAll<HTMLButtonElement>('.option')]
    expect(options.map((o) => o.querySelector('kbd')?.textContent)).toEqual(['1', '2', '3'])
    expect(options.map((o) => o.querySelector('.option-label')?.textContent)).toEqual(['силос', 'дотримуватися', 'зненацька'])
    expect(container.querySelector('.counts')?.textContent).toBe('Питання 1 з 4')
  })

  it('неправильна відповідь: підсвітка, рядок з правильним перекладом, «Далі» тягне наступне', async () => {
    apiMock.nextQuestion.mockResolvedValueOnce(questionOne).mockResolvedValueOnce(questionTwo)
    const { container } = await openQuiz()

    const options = [...container.querySelectorAll<HTMLButtonElement>('.option')]
    await click(options[0]) // силос — неправильно
    await flush()

    expect(apiMock.answer).toHaveBeenCalledWith(5, 100, 2)
    expect(options[0].classList.contains('is-wrong')).toBe(true)
    expect(options[1].classList.contains('is-correct')).toBe(true)
    expect(options.every((o) => o.disabled)).toBe(true)
    expect(container.querySelector('.quiz-result')?.textContent).toBe('abide — дотримуватися')

    await click(buttons(container).find((b) => b.textContent?.includes('Далі'))!)
    await flush()

    expect(apiMock.nextQuestion).toHaveBeenCalledTimes(2)
    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('silo')
    expect(container.querySelector('.quiz-result')).toBeNull()
  })

  it('правильна відповідь показує «Правильно»', async () => {
    apiMock.answer.mockResolvedValue({ isCorrect: true, correctWordPairId: 1, word: 'abide', translation: 'дотримуватися' })
    const { container } = await openQuiz()

    await click([...container.querySelectorAll<HTMLButtonElement>('.option')][1])
    await flush()

    expect(container.querySelector('.quiz-result')?.textContent).toBe('Правильно')
  })

  it('клавіші: 1 обирає перший варіант, Enter — «Далі»; після відповіді цифри ігноруються', async () => {
    apiMock.nextQuestion.mockResolvedValueOnce(questionOne).mockResolvedValueOnce(questionTwo)
    const { container } = await openQuiz()

    await press('1')
    await flush()
    expect(apiMock.answer).toHaveBeenCalledWith(5, 100, 2)

    await press('2')
    await flush()
    expect(apiMock.answer).toHaveBeenCalledTimes(1)

    await press('Enter')
    await flush()
    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('silo')
  })

  it('204 на відповідь (повторний клік) — просто наступне питання', async () => {
    apiMock.answer.mockResolvedValue(null)
    apiMock.nextQuestion.mockResolvedValueOnce(questionOne).mockResolvedValueOnce(questionTwo)
    const { container } = await openQuiz()

    await click([...container.querySelectorAll<HTMLButtonElement>('.option')][0])
    await flush()

    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('silo')
  })

  it('«Знаю» знімає слово й показує повідомлення на наступному питанні', async () => {
    apiMock.nextQuestion.mockResolvedValueOnce(questionOne).mockResolvedValueOnce(questionTwo)
    const { container } = await openQuiz()

    await click(buttons(container).find((b) => b.textContent?.includes('Знаю'))!)
    await flush()

    expect(apiMock.markKnown).toHaveBeenCalledWith(5, 100)
    expect(container.textContent).toContain('abide більше не з’явиться')
    expect(container.querySelector('.quiz-prompt')?.textContent).toBe('silo')
  })

  it('порожня черга → finish → підсумок', async () => {
    apiMock.nextQuestion.mockResolvedValue(exhausted)
    const { container } = await openQuiz()

    expect(apiMock.finish).toHaveBeenCalledWith(5)
    expect(container.querySelector('.summary-score')?.textContent).toBe('3 з 4 · 75%')
    expect(container.querySelector('.summary-badge')?.textContent).toBe('Не пройдено')
  })
})

describe('TrainingScreen — підсумок', () => {
  async function openSummary(overrides: Partial<Parameters<typeof TrainingScreen>[0]> = {}) {
    apiMock.nextQuestion.mockResolvedValue(exhausted)
    const result = await render(screen({ started: { ...started, mode: 'review' }, batchSize: null, ...overrides }))
    await flush()
    return result
  }

  it('слова без помилок першими; терміни словами', async () => {
    const { container } = await openSummary()

    const rows = [...container.querySelectorAll('.summary-row')]
    expect(rows.map((r) => r.querySelector('.summary-word')?.textContent)).toEqual(['silo', 'abide'])
    expect(rows[0].querySelector('.summary-score-cell')?.textContent).toBe('')
    expect(rows[1].querySelector('.summary-score-cell')?.textContent).toBe('1/2')
    expect(rows.map((r) => r.querySelector('.summary-box')?.textContent)).toEqual(['бокс 2', 'бокс 1'])
    expect(rows.map((r) => r.querySelector('.summary-due')?.textContent)).toEqual([
      expect.stringMatching(/^\d{2}\.\d{2}$|^завтра$|^сьогодні$/),
      expect.stringMatching(/^\d{2}\.\d{2}$|^завтра$|^сьогодні$/),
    ])
  })

  it('«Повторити помилки» → retry → знову картки', async () => {
    apiMock.retry.mockResolvedValue({ ...started, trainingId: 6 })
    const { container } = await openSummary()

    await click(buttons(container).find((b) => b.textContent?.includes('Повторити помилки'))!)
    await flush()

    expect(apiMock.retry).toHaveBeenCalledWith(5)
    expect(container.textContent).toContain('Нові слова')
  })

  it('«Ще один батч» лише для нового батча і з параметрами маршруту', async () => {
    apiMock.nextQuestion.mockResolvedValue(exhausted)
    apiMock.startNewBatch.mockResolvedValue({ ...started, trainingId: 7 })

    const { container: review } = await openSummary()
    expect(buttons(review).find((b) => b.textContent?.includes('Ще один батч'))).toBeUndefined()

    // Новий батч відкривається на картках — до підсумку треба пройти «Почати квіз».
    const { container } = await render(screen())
    await click(buttons(container).find((b) => b.textContent?.includes('Почати квіз'))!)
    await flush()
    await click(buttons(container).find((b) => b.textContent?.includes('Ще один батч'))!)
    await flush()

    expect(apiMock.startNewBatch).toHaveBeenCalledWith(7, [11], 10)
    expect(container.textContent).toContain('Нові слова')
  })

  it('без жодного питання — окремий стан', async () => {
    apiMock.finish.mockResolvedValue({ correct: 0, total: 0, ratio: 0, passed: false, words: [] })
    const { container } = await openSummary()

    expect(container.textContent).toContain('Жодного питання не лишилось')
    expect(buttons(container).find((b) => b.textContent?.includes('Повторити помилки'))).toBeUndefined()
  })

  it('«До словника» → onBack', async () => {
    const onBack = vi.fn()
    const { container } = await openSummary({ onBack })

    await click(buttons(container).find((b) => b.textContent?.includes('До словника'))!)

    expect(onBack).toHaveBeenCalledTimes(1)
  })
})
