import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { PronunciationProgress } from '../api/client'
import { click, flush, render } from '../test/render'
import { PronunciationFamiliesScreen } from './PronunciationFamiliesScreen'

const apiMock = vi.hoisted(() => ({ getPronunciationProgress: vi.fn() }))
const supportMock = vi.hoisted(() => ({ isSpeechRecognitionSupported: vi.fn(() => true) }))

vi.mock('../api/client', () => ({ api: apiMock }))
vi.mock('./speechRecognition', () => supportMock)

beforeEach(() => {
  apiMock.getPronunciationProgress.mockReset()
  supportMock.isSpeechRecognitionSupported.mockReset().mockReturnValue(true)
})

describe('PronunciationFamiliesScreen', () => {
  it('shows the unsupported-browser message when SpeechRecognition is missing', async () => {
    supportMock.isSpeechRecognitionSupported.mockReturnValue(false)

    const { container } = await render(<PronunciationFamiliesScreen onOpenFamily={() => {}} />)
    await flush()

    expect(container.textContent).toMatch(/Chrome or Edge/)
    expect(apiMock.getPronunciationProgress).not.toHaveBeenCalled()
  })

  it('renders locked, available, and done tiles', async () => {
    apiMock.getPronunciationProgress.mockResolvedValue({
      families: [
        { key: 'a', title: 'Family A', targetSounds: ['x'], total: 5, mastered: 5, status: 'done' },
        { key: 'b', title: 'Family B', targetSounds: ['y'], total: 5, mastered: 1, status: 'available' },
        { key: 'c', title: 'Family C', targetSounds: ['z'], total: 5, mastered: 0, status: 'locked' },
      ],
    } satisfies PronunciationProgress)

    const { container } = await render(<PronunciationFamiliesScreen onOpenFamily={() => {}} />)
    await flush()

    expect(container.textContent).toContain('Family A')
    expect(container.textContent).toContain('Family B')
    expect(container.textContent).toContain('Family C')
    const lockedButton = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Locked')
    expect(lockedButton).toBeDefined()
    expect(lockedButton?.hasAttribute('disabled')).toBe(true)
  })

  it('calls onOpenFamily when an available tile is opened', async () => {
    apiMock.getPronunciationProgress.mockResolvedValue({
      families: [{ key: 'b', title: 'Family B', targetSounds: ['y'], total: 5, mastered: 1, status: 'available' }],
    } satisfies PronunciationProgress)
    const onOpenFamily = vi.fn()

    const { container } = await render(<PronunciationFamiliesScreen onOpenFamily={onOpenFamily} />)
    await flush()

    const openButton = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Open')
    await click(openButton!)

    expect(onOpenFamily).toHaveBeenCalledWith('b')
  })
})
