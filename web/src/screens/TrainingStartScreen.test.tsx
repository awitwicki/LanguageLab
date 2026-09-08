import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { BatchCandidate, BatchPreview, TrainingStarted } from '../api/client'
import { click, flush, render } from '../test/render'
import { TrainingStartScreen } from './TrainingStartScreen'

const apiMock = vi.hoisted(() => ({ startNewBatch: vi.fn(), previewBatch: vi.fn(), mark: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const started: TrainingStarted = { trainingId: 5, mode: 'newBatch', words: [], totalQuestions: 20 }

const candidate = (i: number): BatchCandidate => ({
  wordPairId: 100 + i,
  word: `word${i}`,
  translation: `переклад${i}`,
  frequency: 500 - i,
})

const preview: BatchPreview = {
  learning: { notStarted: 42, boxes: [9, 5, 0, 3, 0], learned: 12, total: 71 },
  learnableCount: 42,
  candidates: Array.from({ length: 20 }, (_, i) => candidate(i)),
}

const emptyPreview: BatchPreview = {
  learning: { notStarted: 0, boxes: [0, 0, 0, 0, 0], learned: 0, total: 0 },
  learnableCount: 0,
  candidates: [],
}

function screen(overrides: Partial<Parameters<typeof TrainingStartScreen>[0]> = {}) {
  return (
    <TrainingStartScreen
      dictionaryId={7}
      dictionaryName="Wool"
      chapterIds={[11]}
      scopeTitle="Holston"
      onStarted={() => {}}
      onBack={() => {}}
      {...overrides}
    />
  )
}

function buttons(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('button')]
}

function rows(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLElement>('.batch-row')]
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.startNewBatch.mockResolvedValue(started)
  apiMock.previewBatch.mockResolvedValue(preview)
  apiMock.mark.mockResolvedValue(null)
})

describe('TrainingStartScreen — превью', () => {
  it('тягне превью один раз (take 20); шкала, слова з перекладом і частотою; зріз 5/10/20 без нового запиту', async () => {
    const { container } = await render(screen())
    await flush()

    expect(apiMock.previewBatch).toHaveBeenCalledWith(7, [11], 20)
    expect(container.querySelector('.leitner-large')).not.toBeNull()
    expect(container.textContent).toContain('42 слова до вивчення')
    expect(container.textContent).toContain('частота в главі')

    expect(rows(container)).toHaveLength(10)
    expect(rows(container)[0].textContent).toContain('word0')
    expect(rows(container)[0].textContent).toContain('переклад0')
    expect(rows(container)[0].querySelector('.batch-freq')?.textContent).toBe('500')

    const radios = [...container.querySelectorAll<HTMLButtonElement>('[role="radio"]')]
    await click(radios[2])
    expect(rows(container)).toHaveLength(20)
    await click(radios[0])
    expect(rows(container)).toHaveLength(5)
    expect(apiMock.previewBatch).toHaveBeenCalledTimes(1)
  })

  it('«Почати» передає id активних рядків і розмір батча', async () => {
    const onStarted = vi.fn()
    const { container } = await render(screen({ onStarted }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Почати')!)
    await flush()

    expect(apiMock.startNewBatch).toHaveBeenCalledWith(7, [11], 10, [100, 101, 102, 103, 104, 105, 106, 107, 108, 109])
    expect(onStarted).toHaveBeenCalledWith(started, 10)
  })

  it('× → mark known, рядок викреслений на місці, заміна знизу; «Повернути» → mark unknown', async () => {
    const { container } = await render(screen())
    await flush()

    apiMock.previewBatch.mockResolvedValueOnce({
      ...preview,
      candidates: preview.candidates.filter((c) => c.wordPairId !== 101),
    })
    await click(rows(container)[1].querySelector('.batch-know')!)
    await flush()

    expect(apiMock.mark).toHaveBeenCalledWith(101, 'known')
    expect(apiMock.previewBatch).toHaveBeenCalledTimes(2)
    expect(rows(container)).toHaveLength(11)
    expect(rows(container)[1].classList.contains('is-struck')).toBe(true)
    expect(rows(container)[1].textContent).toContain('word1')
    expect(rows(container)[10].textContent).toContain('word10')

    apiMock.previewBatch.mockResolvedValueOnce(preview)
    await click(rows(container)[1].querySelector('button')!)
    await flush()

    expect(apiMock.mark).toHaveBeenCalledWith(101, 'unknown')
    expect(rows(container)).toHaveLength(10)
    expect(container.querySelector('.is-struck')).toBeNull()
  })

  it('«Почати» після викреслення не містить викресленого id', async () => {
    const { container } = await render(screen())
    await flush()

    apiMock.previewBatch.mockResolvedValueOnce({
      ...preview,
      candidates: preview.candidates.filter((c) => c.wordPairId !== 100),
    })
    await click(rows(container)[0].querySelector('.batch-know')!)
    await flush()
    await click(buttons(container).find((b) => b.textContent === 'Почати')!)
    await flush()

    expect(apiMock.startNewBatch).toHaveBeenCalledWith(7, [11], 10, [101, 102, 103, 104, 105, 106, 107, 108, 109, 110])
  })

  it('порожнє превью: підказка, таблички немає, «Почати» неактивна', async () => {
    apiMock.previewBatch.mockResolvedValue(emptyPreview)
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.batch-preview')).toBeNull()
    expect(container.textContent).toContain('немає слів до вивчення')
    expect(buttons(container).find((b) => b.textContent === 'Почати')!.disabled).toBe(true)
  })

  it('коли слів менше за розмір батча — попередження', async () => {
    apiMock.previewBatch.mockResolvedValue({ ...preview, learnableCount: 7, candidates: preview.candidates.slice(0, 7) })
    const { container } = await render(screen())
    await flush()

    expect(container.textContent).toContain('батч буде з 7 слів')
  })

  it('уся книжка → частота підписана «в книжці»', async () => {
    const { container } = await render(screen({ chapterIds: null, scopeTitle: 'Уся книжка' }))
    await flush()

    expect(apiMock.previewBatch).toHaveBeenCalledWith(7, null, 20)
    expect(container.textContent).toContain('частота в книжці')
  })
})

describe('TrainingStartScreen — старт і навігація', () => {
  it('204 (null) → пояснення, onStarted не викликається', async () => {
    apiMock.startNewBatch.mockResolvedValue(null)
    const onStarted = vi.fn()
    const { container } = await render(screen({ onStarted }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Почати')!)
    await flush()

    expect(onStarted).not.toHaveBeenCalled()
    expect(container.textContent).toContain('немає слів до вивчення')
  })

  it('поки превью не приїхало — «Завантажую…», «Почати» неактивна', async () => {
    apiMock.previewBatch.mockReturnValue(new Promise(() => {}))
    const { container } = await render(screen())

    expect(container.textContent).toContain('Завантажую')
    expect(buttons(container).find((b) => b.textContent === 'Почати')!.disabled).toBe(true)
  })

  it('«‹ Wool» → onBack; підзаголовок містить скоуп', async () => {
    const onBack = vi.fn()
    const { container } = await render(screen({ onBack }))
    await flush()

    expect(container.textContent).toContain('Wool · Holston')
    await click(buttons(container).find((b) => b.textContent?.includes('Wool'))!)

    expect(onBack).toHaveBeenCalledTimes(1)
  })
})
