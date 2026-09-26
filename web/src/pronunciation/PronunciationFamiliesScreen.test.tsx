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

    const { container } = await render(<PronunciationFamiliesScreen onOpenFamily={() => {}} onOpenAlphabet={() => {}} />)
    await flush()

    expect(container.textContent).toMatch(/Chrome or Edge/)
    expect(apiMock.getPronunciationProgress).not.toHaveBeenCalled()
    // The alphabet asks nothing of the browser, so it is the one thing still on offer.
    expect(container.querySelector('.alphabet-card')).not.toBeNull()
  })

  it('opens the alphabet from a browser that cannot practise', async () => {
    supportMock.isSpeechRecognitionSupported.mockReturnValue(false)
    const onOpenAlphabet = vi.fn()

    const { container } = await render(
      <PronunciationFamiliesScreen onOpenFamily={() => {}} onOpenAlphabet={onOpenAlphabet} />,
    )
    await flush()

    await click(container.querySelector('.alphabet-card .btn')!)

    expect(onOpenAlphabet).toHaveBeenCalled()
  })

  it('offers the alphabet beside the families when practice does work', async () => {
    apiMock.getPronunciationProgress.mockResolvedValue({
      families: [{ key: 'b', title: 'Family B', targetSounds: ['y'], total: 5, mastered: 1, status: 'available' }],
    } satisfies PronunciationProgress)
    const onOpenAlphabet = vi.fn()

    const { container } = await render(
      <PronunciationFamiliesScreen onOpenFamily={() => {}} onOpenAlphabet={onOpenAlphabet} />,
    )
    await flush()

    await click(container.querySelector('.alphabet-card .btn')!)

    expect(onOpenAlphabet).toHaveBeenCalled()
    expect(container.querySelectorAll('.family-tile')).toHaveLength(1)
  })

  it('renders done and untouched families alike, every tile open', async () => {
    apiMock.getPronunciationProgress.mockResolvedValue({
      families: [
        { key: 'a', title: 'Family A', targetSounds: ['x'], total: 5, mastered: 5, status: 'done' },
        { key: 'b', title: 'Family B', targetSounds: ['y'], total: 5, mastered: 1, status: 'available' },
        { key: 'c', title: 'Family C', targetSounds: ['z'], total: 5, mastered: 0, status: 'available' },
      ],
    } satisfies PronunciationProgress)

    const { container } = await render(<PronunciationFamiliesScreen onOpenFamily={() => {}} onOpenAlphabet={() => {}} />)
    await flush()

    expect(container.textContent).toContain('Family A')
    expect(container.textContent).toContain('Family B')
    expect(container.textContent).toContain('Family C')
    const buttons = [...container.querySelectorAll('.family-tile .btn')]
    expect(buttons.map((b) => b.textContent)).toEqual(['Open', 'Open', 'Open'])
    expect(buttons.some((b) => b.hasAttribute('disabled'))).toBe(false)
  })

  it('calls onOpenFamily when an available tile is opened', async () => {
    apiMock.getPronunciationProgress.mockResolvedValue({
      families: [{ key: 'b', title: 'Family B', targetSounds: ['y'], total: 5, mastered: 1, status: 'available' }],
    } satisfies PronunciationProgress)
    const onOpenFamily = vi.fn()

    const { container } = await render(<PronunciationFamiliesScreen onOpenFamily={onOpenFamily} onOpenAlphabet={() => {}} />)
    await flush()

    const openButton = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Open')
    await click(openButton!)

    expect(onOpenFamily).toHaveBeenCalledWith('b')
  })
})
