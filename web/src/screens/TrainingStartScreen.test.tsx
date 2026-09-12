import { act } from 'react'
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

function radios(container: HTMLElement) {
  return [...container.querySelectorAll<HTMLButtonElement>('[role="radio"]')]
}

function press(target: Element, key: string) {
  return act(async () => {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  apiMock.startNewBatch.mockResolvedValue(started)
  apiMock.previewBatch.mockResolvedValue(preview)
  apiMock.mark.mockResolvedValue(null)
})

describe('TrainingStartScreen — preview', () => {
  it('fetches the preview once (take 20); scale, words with translation and frequency; 5/10/20 slices without a new request', async () => {
    const { container } = await render(screen())
    await flush()

    expect(apiMock.previewBatch).toHaveBeenCalledWith(7, [11], 20)
    expect(container.querySelector('.leitner-large')).not.toBeNull()
    expect(container.textContent).toContain('42 words to learn')
    expect(container.textContent).toContain('frequency in the chapter')

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

  it('"Start" passes the active row ids and the batch size', async () => {
    const onStarted = vi.fn()
    const { container } = await render(screen({ onStarted }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Start')!)
    await flush()

    expect(apiMock.startNewBatch).toHaveBeenCalledWith(7, [11], 10, [100, 101, 102, 103, 104, 105, 106, 107, 108, 109])
    expect(onStarted).toHaveBeenCalledWith(started, 10)
  })

  it('× → mark known, the row is crossed out in place, a replacement appended; "Bring back" → mark unknown', async () => {
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

  it('"Start" after a cross-out omits the crossed-out id', async () => {
    const { container } = await render(screen())
    await flush()

    apiMock.previewBatch.mockResolvedValueOnce({
      ...preview,
      candidates: preview.candidates.filter((c) => c.wordPairId !== 100),
    })
    await click(rows(container)[0].querySelector('.batch-know')!)
    await flush()
    await click(buttons(container).find((b) => b.textContent === 'Start')!)
    await flush()

    expect(apiMock.startNewBatch).toHaveBeenCalledWith(7, [11], 10, [101, 102, 103, 104, 105, 106, 107, 108, 109, 110])
  })

  it('an empty preview: a hint, no table, "Start" disabled', async () => {
    apiMock.previewBatch.mockResolvedValue(emptyPreview)
    const { container } = await render(screen())
    await flush()

    expect(container.querySelector('.batch-preview')).toBeNull()
    expect(container.textContent).toContain('No words to learn in this set')
    expect(buttons(container).find((b) => b.textContent === 'Start')!.disabled).toBe(true)
  })

  it('fewer words than the batch size — a warning', async () => {
    apiMock.previewBatch.mockResolvedValue({ ...preview, learnableCount: 7, candidates: preview.candidates.slice(0, 7) })
    const { container } = await render(screen())
    await flush()

    expect(container.textContent).toContain('the batch will have 7 words')
  })

  it('whole book → frequency captioned "in the book"', async () => {
    const { container } = await render(screen({ chapterIds: null, scopeTitle: 'Whole book' }))
    await flush()

    expect(apiMock.previewBatch).toHaveBeenCalledWith(7, null, 20)
    expect(container.textContent).toContain('frequency in the book')
  })
})

describe('TrainingStartScreen — start and navigation', () => {
  it('204 (null) → an explanation, onStarted not called', async () => {
    apiMock.startNewBatch.mockResolvedValue(null)
    const onStarted = vi.fn()
    const { container } = await render(screen({ onStarted }))
    await flush()

    await click(buttons(container).find((b) => b.textContent === 'Start')!)
    await flush()

    expect(onStarted).not.toHaveBeenCalled()
    expect(container.textContent).toContain('No words to learn in this set')
  })

  it('until the preview arrives — "Loading…", "Start" disabled', async () => {
    apiMock.previewBatch.mockReturnValue(new Promise(() => {}))
    const { container } = await render(screen())

    expect(container.textContent).toContain('Loading')
    expect(buttons(container).find((b) => b.textContent === 'Start')!.disabled).toBe(true)
  })

  it('"‹ Wool" → onBack; the subtitle carries the scope', async () => {
    const onBack = vi.fn()
    const { container } = await render(screen({ onBack }))
    await flush()

    expect(container.textContent).toContain('Wool · Holston')
    await click(buttons(container).find((b) => b.textContent?.includes('Wool'))!)

    expect(onBack).toHaveBeenCalledTimes(1)
  })
})

describe('TrainingStartScreen — batch-size radiogroup', () => {
  it('only the checked size is in the tab order (roving tabindex)', async () => {
    const { container } = await render(screen())
    await flush()

    expect(radios(container).map((r) => r.tabIndex)).toEqual([-1, 0, -1])

    await click(radios(container)[2])
    expect(radios(container).map((r) => r.tabIndex)).toEqual([-1, -1, 0])
  })

  it('arrow keys check the neighbour and move focus to it, wrapping at both ends', async () => {
    const { container } = await render(screen())
    await flush()

    radios(container)[1].focus()
    await press(radios(container)[1], 'ArrowRight')
    expect(radios(container).map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'false', 'true'])
    expect(document.activeElement).toBe(radios(container)[2])
    expect(rows(container)).toHaveLength(20)

    await press(radios(container)[2], 'ArrowDown')
    expect(document.activeElement).toBe(radios(container)[0])
    expect(rows(container)).toHaveLength(5)

    await press(radios(container)[0], 'ArrowLeft')
    expect(document.activeElement).toBe(radios(container)[2])

    await press(radios(container)[2], 'ArrowUp')
    expect(document.activeElement).toBe(radios(container)[1])
    expect(rows(container)).toHaveLength(10)
  })

  it('other keys leave the group alone', async () => {
    const { container } = await render(screen())
    await flush()

    radios(container)[1].focus()
    await press(radios(container)[1], 'Tab')

    expect(document.activeElement).toBe(radios(container)[1])
    expect(rows(container)).toHaveLength(10)
  })
})
