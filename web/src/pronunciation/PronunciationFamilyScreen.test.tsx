import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { click, flush, render } from '../test/render'
import type { Clip } from './audio/decodeClip'
import type { ClipAnalysis } from './audio/spectrogram'
import { PronunciationFamilyScreen } from './PronunciationFamilyScreen'
import type { SpeechRecognitionErrorEvent, SpeechRecognitionLike } from './speechRecognition'
import type { UsePronunciationFamilyResult } from './usePronunciationFamily'

const hookMock = vi.hoisted(() => ({ usePronunciationFamily: vi.fn() }))
const recognitionMock = vi.hoisted(() => ({ createSpeechRecognition: vi.fn() }))
const clipMock = vi.hoisted(() => ({ useClipAnalysis: vi.fn() }))
const micMock = vi.hoisted(() => ({
  start: vi.fn(),
  liveFrame: vi.fn(),
  stop: vi.fn(),
  isMicSupported: vi.fn(() => true),
}))

vi.mock('./usePronunciationFamily', () => hookMock)
// Partial mock: only the constructor and its support check are faked, so transcriptOf
// still does the real unwrapping of a recognition result the screen depends on.
// jsdom never defines window.SpeechRecognition, so the support check is stubbed true
// rather than making every test set up that global.
vi.mock('./speechRecognition', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./speechRecognition')>()),
  createSpeechRecognition: recognitionMock.createSpeechRecognition,
  isSpeechRecognitionSupported: () => true,
}))
vi.mock('./audio/useClipAnalysis', () => clipMock)
vi.mock('./audio/useMicCapture', () => ({
  useMicCapture: () => ({ start: micMock.start, liveFrame: micMock.liveFrame, stop: micMock.stop }),
  isMicSupported: micMock.isMicSupported,
}))

const RATE = 16000
const toneClip: Clip = {
  samples: Float32Array.from({ length: RATE }, (_, i) => 0.5 * Math.sin((2 * Math.PI * 440 * i) / RATE)),
  sampleRate: RATE,
}

const analysis: ClipAnalysis = {
  spectrogram: { columns: [new Float32Array(512).fill(-30)], binHz: 46.875, hopSeconds: 0.005 },
  duration: 0.7,
  trimStart: 0.1,
}

let hookResult: UsePronunciationFamilyResult

/** Fresh mocks for every test — a shared module-scope object would carry calls across them. */
function freshHookResult(): UsePronunciationFamilyResult {
  return {
    status: 'ready',
    error: null,
    attemptError: null,
    title: 'Ɪ vs Iː',
    word: { word: 'ship', ipa: '/ʃɪp/', audioUs: '/a-us.ogg', audioUk: '/a-uk.ogg', state: 'new', streak: 0 },
    accent: 'us',
    feedback: null,
    setAccent: vi.fn(),
    submitTranscript: vi.fn(),
    reportAttemptError: vi.fn(),
    next: vi.fn(),
    practiceAgain: vi.fn(),
  }
}

function fakeRecognition(start: () => void = vi.fn()): SpeechRecognitionLike {
  return {
    lang: '',
    continuous: false,
    interimResults: false,
    maxAlternatives: 1,
    start,
    stop: vi.fn(),
    abort: vi.fn(),
    onresult: null,
    onerror: null,
    onend: null,
  } as unknown as SpeechRecognitionLike
}

function show(overrides: Partial<UsePronunciationFamilyResult> = {}) {
  hookResult = { ...hookResult, ...overrides }
  return render(<PronunciationFamilyScreen familyKey="ih-vs-iy" onBack={() => {}} />)
}

function buttonLabelled(container: HTMLElement, label: string) {
  return [...container.querySelectorAll('button')].find((b) => b.textContent === label)
}

function attemptStrip(container: HTMLElement) {
  return container.querySelectorAll('.pronunciation-strips .spectrogram-strip')[1]
}

async function record(container: HTMLElement) {
  await click(buttonLabelled(container, 'Record')!)
  await flush()
}

beforeEach(() => {
  hookResult = freshHookResult()
  hookMock.usePronunciationFamily.mockReset().mockImplementation(() => hookResult)
  recognitionMock.createSpeechRecognition.mockReset().mockReturnValue(fakeRecognition())
  clipMock.useClipAnalysis.mockReset().mockReturnValue({ status: 'ready', analysis })
  micMock.start.mockReset().mockResolvedValue(undefined)
  micMock.liveFrame.mockReset().mockReturnValue(null)
  micMock.stop.mockReset().mockResolvedValue(toneClip)
  micMock.isMicSupported.mockReturnValue(true)
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
    () =>
      ({
        putImageData: vi.fn(),
        fillRect: vi.fn(),
        drawImage: vi.fn(),
        createImageData: (w: number, h: number) => ({ data: new Uint8ClampedArray(w * h * 4), width: w, height: h }),
        fillStyle: '',
      }) as unknown as CanvasRenderingContext2D,
  )
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(() => Promise.resolve())
  vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => {})
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('PronunciationFamilyScreen', () => {
  it('shows the word and its IPA', async () => {
    const { container } = await show()

    expect(container.textContent).toContain('ship')
    expect(container.textContent).toContain('/ʃɪp/')
  })

  it('shows the family-complete state as a closeable dialog with a practice-again action', async () => {
    const { container } = await show({ status: 'family-complete', word: null })

    const dialog = container.querySelector('[role="dialog"]')
    expect(dialog).not.toBeNull()
    expect(dialog!.textContent).toMatch(/mastered every word/i)
    expect(buttonLabelled(container, 'Practice again')).toBeDefined()
    expect(buttonLabelled(container, 'Back to lessons')).toBeDefined()
  })

  it('closes the family-complete dialog on Escape', async () => {
    const onBack = vi.fn()
    hookResult = { ...freshHookResult(), status: 'family-complete', word: null }
    await render(<PronunciationFamilyScreen familyKey="ih-vs-iy" onBack={onBack} />)

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    })

    expect(onBack).toHaveBeenCalled()
  })

  it('shows feedback and a Next button after an attempt', async () => {
    const { container } = await show({
      status: 'feedback',
      feedback: { outcome: 'correct', score: 92, state: 'learning', streak: 1, familyDone: false },
    })

    expect(container.textContent).toContain('92')
    expect(buttonLabelled(container, 'Next')).toBeDefined()
  })

  it('toggles accent on click', async () => {
    const { container } = await show()

    await click(buttonLabelled(container, 'UK')!)

    expect(hookResult.setAccent).toHaveBeenCalledWith('uk')
  })

  it('submits what the recognizer heard', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()

    await record(container)
    await act(async () => {
      recognition.onresult?.({ results: [[{ transcript: 'ship' }]] } as never)
    })

    expect(hookResult.submitTranscript).toHaveBeenCalledWith('ship')
  })

  it('explains a denied microphone instead of failing silently', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()

    await record(container)
    await act(async () => {
      recognition.onerror?.({ error: 'not-allowed' } as SpeechRecognitionErrorEvent)
    })

    expect(hookResult.reportAttemptError).toHaveBeenCalledWith(expect.stringMatching(/Microphone access was denied/))
  })

  it('reports any other recognition failure as a retry prompt', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()

    await record(container)
    await act(async () => {
      recognition.onerror?.({ error: 'no-speech' } as SpeechRecognitionErrorEvent)
    })

    expect(hookResult.reportAttemptError).toHaveBeenCalledWith(expect.stringMatching(/Couldn't hear you/))
  })

  it('survives a recognizer that throws on start', async () => {
    // start() throws InvalidStateError when a previous recognition is still running.
    recognitionMock.createSpeechRecognition.mockReturnValue(
      fakeRecognition(() => {
        throw new Error('InvalidStateError')
      }),
    )
    const { container } = await show()

    await record(container)

    expect(hookResult.reportAttemptError).toHaveBeenCalledWith(expect.stringMatching(/Couldn't start recording/))
    // The button went back to idle, so the learner can press it again.
    expect(buttonLabelled(container, 'Record')?.hasAttribute('disabled')).toBe(false)
  })

  it('never creates a recognizer if the screen unmounts while mic.start() is still pending', async () => {
    let resolveStart: () => void = () => {}
    micMock.start.mockReset().mockReturnValue(
      new Promise<void>((resolve) => {
        resolveStart = resolve
      }),
    )
    const { container, unmount } = await show()

    await click(buttonLabelled(container, 'Record')!)
    await unmount()

    await act(async () => {
      resolveStart()
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(recognitionMock.createSpeechRecognition).not.toHaveBeenCalled()
  })

  it('aborts a running recognizer when the screen goes away and drops its late result', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container, unmount } = await show()

    await record(container)
    await unmount()

    expect(recognition.abort).toHaveBeenCalledTimes(1)

    await act(async () => {
      recognition.onresult?.({ results: [[{ transcript: 'ship' }]] } as never)
    })

    expect(hookResult.submitTranscript).not.toHaveBeenCalled()
  })

  it('keeps Record disabled and says so while the attempt is being scored', async () => {
    const { container } = await show({ status: 'scoring' })

    const button = container.querySelector<HTMLButtonElement>('.pronunciation-controls button')!

    expect(button.textContent).toBe('Scoring…')
    expect(button.disabled).toBe(true)
  })

  it('shows how far the streak is from mastery', async () => {
    const { container } = await show({
      status: 'feedback',
      feedback: { outcome: 'correct', score: 92, state: 'learning', streak: 2, familyDone: false },
    })

    expect(container.querySelector('.pronunciation-feedback')?.textContent).toContain('2 of 3')
  })

  it('celebrates mastery instead of counting the streak', async () => {
    const { container } = await show({
      status: 'feedback',
      feedback: { outcome: 'correct', score: 100, state: 'mastered', streak: 3, familyDone: false },
    })

    expect(container.querySelector('.pronunciation-feedback')?.textContent).toMatch(/mastered/i)
    expect(container.querySelector('.pronunciation-feedback')?.textContent).not.toContain('of 3')
  })

  it('keeps the word card in place while the next word loads', async () => {
    const { container } = await show({ status: 'loading' })

    expect(container.querySelector('.pronunciation-word')?.textContent).toBe('ship')
    expect(container.textContent).not.toContain('Loading')
    expect(buttonLabelled(container, 'Record')?.hasAttribute('disabled')).toBe(true)
  })

  it('shows a placeholder only before the first word has arrived', async () => {
    const { container } = await show({ status: 'loading', word: null })

    expect(container.textContent).toContain('Loading')
  })

  it('renders an attempt error next to a Record button that still works', async () => {
    const { container } = await show({ attemptError: "Couldn't score that attempt — check your connection." })

    expect(container.textContent).toContain("Couldn't score that attempt")
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
    const recordButton = buttonLabelled(container, 'Record')
    expect(recordButton?.hasAttribute('disabled')).toBe(false)

    await record(container)

    expect(recognitionMock.createSpeechRecognition).toHaveBeenCalled()
  })
})

describe('reference strip', () => {
  it('asks for the clip of the chosen accent', async () => {
    await show({ accent: 'uk' })

    expect(clipMock.useClipAnalysis).toHaveBeenLastCalledWith('/a-uk.ogg')
  })

  it('shows the reference spectrogram with its duration', async () => {
    const { container } = await show()

    const strip = container.querySelector('.pronunciation-strips .spectrogram-strip')!
    expect(strip.querySelector('.spectrogram-strip-label')?.textContent).toBe('Reference')
    expect(strip.querySelector('canvas')).not.toBeNull()
    expect(strip.querySelector('.spectrogram-strip-duration')?.textContent).toBe('0.7 s')
  })

  it('says "No preview" when the clip cannot be decoded, and Play still works', async () => {
    clipMock.useClipAnalysis.mockReturnValue({ status: 'failed' })
    const { container } = await show()

    expect(container.querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('No preview')
    await click(buttonLabelled(container, 'Play')!)
    expect(HTMLMediaElement.prototype.play).toHaveBeenCalled()
  })

  it('disables Play while listening, so the reference clip cannot leak into the microphone', async () => {
    const { container } = await show()

    await record(container)

    expect(buttonLabelled(container, 'Play')?.hasAttribute('disabled')).toBe(true)
  })

  it('has no native audio controls any more', async () => {
    const { container } = await show()

    expect(container.querySelector('audio')?.hasAttribute('controls')).toBe(false)
  })

  it('reads "Playing…" while the clip plays', async () => {
    const { container } = await show()
    const audio = container.querySelector('audio')!

    await act(async () => {
      audio.dispatchEvent(new Event('play'))
    })
    expect(buttonLabelled(container, 'Playing…')).toBeDefined()

    await act(async () => {
      audio.dispatchEvent(new Event('ended'))
    })
    expect(buttonLabelled(container, 'Play')).toBeDefined()
  })
})

describe('attempt strip', () => {
  it('starts as "Press Record"', async () => {
    const { container } = await show()

    expect(attemptStrip(container).querySelector('.spectrogram-strip-label')?.textContent).toBe('Your attempt')
    expect(attemptStrip(container).querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('Press Record')
  })

  it('Record pauses the reference, opens the microphone, then starts the recognizer', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()

    await record(container)

    expect(HTMLMediaElement.prototype.pause).toHaveBeenCalled()
    expect(micMock.start).toHaveBeenCalledTimes(1)
    expect(recognition.start).toHaveBeenCalledTimes(1)
    expect(attemptStrip(container).querySelector('canvas')).not.toBeNull()
  })

  it('a denied microphone reports the permission message and never creates a recognizer', async () => {
    micMock.start.mockRejectedValue(Object.assign(new Error('denied'), { name: 'NotAllowedError' }))
    const { container } = await show()

    await record(container)

    expect(hookResult.reportAttemptError).toHaveBeenCalledWith(expect.stringMatching(/Microphone access was denied/))
    expect(recognitionMock.createSpeechRecognition).not.toHaveBeenCalled()
    expect(buttonLabelled(container, 'Record')?.hasAttribute('disabled')).toBe(false)
  })

  it('any other microphone failure asks to try again', async () => {
    micMock.start.mockRejectedValue(new Error('NotReadableError'))
    const { container } = await show()

    await record(container)

    expect(hookResult.reportAttemptError).toHaveBeenCalledWith(expect.stringMatching(/Couldn't start recording/))
  })

  it('when the recognizer ends, the microphone stops and the strip freezes with a duration', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()
    await record(container)

    await act(async () => {
      recognition.onend?.()
    })
    await flush()

    expect(micMock.stop).toHaveBeenCalledTimes(1)
    expect(attemptStrip(container).querySelector('.spectrogram-strip-duration')?.textContent).toMatch(/\d\.\d s/)
  })

  it('says "Nothing captured" when the recording came back empty', async () => {
    micMock.stop.mockResolvedValue(null)
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()
    await record(container)

    await act(async () => {
      recognition.onend?.()
    })
    await flush()

    expect(attemptStrip(container).querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('Nothing captured')
  })

  it('a recognizer that fails to start releases the microphone', async () => {
    recognitionMock.createSpeechRecognition.mockReturnValue(
      fakeRecognition(() => {
        throw new Error('InvalidStateError')
      }),
    )
    const { container } = await show()

    await record(container)

    expect(micMock.stop).toHaveBeenCalledTimes(1)
    expect(attemptStrip(container).querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('Press Record')
  })

  it('Next clears the attempt strip', async () => {
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container, rerender } = await show()
    await record(container)
    await act(async () => {
      recognition.onend?.()
    })
    await flush()

    hookResult = {
      ...hookResult,
      status: 'feedback',
      feedback: { outcome: 'correct', score: 90, state: 'learning', streak: 1, familyDone: false },
    }
    await rerender(<PronunciationFamilyScreen familyKey="ih-vs-iy" onBack={() => {}} />)
    await click(buttonLabelled(container, 'Next')!)

    expect(hookResult.next).toHaveBeenCalled()
    expect(attemptStrip(container).querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('Press Record')
  })

  it('without microphone capture the strips explain themselves and Record still recognizes', async () => {
    micMock.isMicSupported.mockReturnValue(false)
    const recognition = fakeRecognition()
    recognitionMock.createSpeechRecognition.mockReturnValue(recognition)
    const { container } = await show()

    expect(attemptStrip(container).querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('Spectrogram needs a newer browser')
    await record(container)

    expect(micMock.start).not.toHaveBeenCalled()
    expect(recognition.start).toHaveBeenCalledTimes(1)
  })
})
