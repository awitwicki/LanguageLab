import { act, createElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { isMicSupported, useMicCapture } from './useMicCapture'

const decodeMock = vi.hoisted(() => ({ decodeAudio: vi.fn() }))
vi.mock('./decodeClip', () => decodeMock)
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

const tracks = [{ stop: vi.fn() }, { stop: vi.fn() }]
const stream = { getTracks: () => tracks }
const getUserMedia = vi.fn()
const analyser = {
  fftSize: 2048,
  smoothingTimeConstant: 0.8,
  minDecibels: 0,
  maxDecibels: 0,
  frequencyBinCount: 512,
  getByteFrequencyData: vi.fn((out: Uint8Array) => out.fill(7)),
}
const close = vi.fn(() => Promise.resolve())
class FakeAudioContext {
  sampleRate = 48000
  close = close
  createAnalyser() {
    return analyser
  }
  createMediaStreamSource() {
    return { connect: vi.fn() }
  }
}
class FakeRecorder {
  state: 'inactive' | 'recording' = 'inactive'
  mimeType = 'audio/webm'
  ondataavailable: ((e: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null
  start() {
    this.state = 'recording'
  }
  stop() {
    this.state = 'inactive'
    this.ondataavailable?.({ data: new Blob([new Uint8Array([1, 2, 3])]) })
    this.onstop?.()
  }
}
let recorder: FakeRecorder | null = null
// Track the instance the hook constructs from outside, instead of aliasing `this` in a constructor.
const FakeMediaRecorder = new Proxy(FakeRecorder, {
  construct(target, args) {
    const instance = Reflect.construct(target, args)
    recorder = instance
    return instance
  },
})

let root: Root | null = null
let container: HTMLElement

async function renderHook<T>(hook: () => T): Promise<{ current: T }> {
  const result = { current: undefined as unknown as T }
  function Probe() {
    result.current = hook()
    return null
  }
  container = document.createElement('div')
  document.body.appendChild(container)
  root = createRoot(container)
  await act(async () => root!.render(createElement(Probe)))
  return result
}

async function unmountHook() {
  const current = root
  root = null
  if (current) await act(async () => current.unmount())
}

beforeEach(() => {
  recorder = null
  for (const t of tracks) t.stop.mockReset()
  close.mockClear()
  getUserMedia.mockReset().mockResolvedValue(stream)
  decodeMock.decodeAudio.mockReset().mockResolvedValue({ samples: new Float32Array([0.1]), sampleRate: 48000 })
  vi.stubGlobal('AudioContext', FakeAudioContext)
  vi.stubGlobal('MediaRecorder', FakeMediaRecorder)
  vi.stubGlobal('navigator', { mediaDevices: { getUserMedia } })
})

afterEach(async () => {
  await unmountHook()
  container?.remove()
  vi.unstubAllGlobals()
})

describe('isMicSupported', () => {
  it('needs AudioContext, MediaRecorder and getUserMedia', () => {
    expect(isMicSupported()).toBe(true)
    vi.stubGlobal('MediaRecorder', undefined)
    expect(isMicSupported()).toBe(false)
  })
})

describe('useMicCapture', () => {
  it('reports nothing before start', async () => {
    const mic = await renderHook(() => useMicCapture())

    expect(mic.current.liveFrame()).toBeNull()
    await expect(mic.current.stop()).resolves.toBeNull()
  })

  it('opens the microphone, an unsmoothed 1024-point analyser and a recorder', async () => {
    const mic = await renderHook(() => useMicCapture())

    await act(() => mic.current.start())

    expect(getUserMedia).toHaveBeenCalledWith({ audio: true })
    expect(analyser.fftSize).toBe(1024)
    expect(analyser.smoothingTimeConstant).toBe(0)
    expect(recorder?.state).toBe('recording')
  })

  it('narrows the analyser to the same 60 dB window as the frozen spectrogram', async () => {
    const mic = await renderHook(() => useMicCapture())

    await act(() => mic.current.start())

    expect(analyser.minDecibels).toBe(-100)
    expect(analyser.maxDecibels).toBe(-40)
  })

  it('serves live frames with the bin width', async () => {
    const mic = await renderHook(() => useMicCapture())
    await act(() => mic.current.start())

    const frame = mic.current.liveFrame()

    expect(frame?.binHz).toBeCloseTo(48000 / 1024)
    expect(frame?.bins[0]).toBe(7)
  })

  it('rejects with the getUserMedia error and starts nothing', async () => {
    const denied = Object.assign(new Error('denied'), { name: 'NotAllowedError' })
    getUserMedia.mockRejectedValue(denied)
    const mic = await renderHook(() => useMicCapture())

    await expect(mic.current.start()).rejects.toBe(denied)
    expect(recorder).toBeNull()
    expect(mic.current.liveFrame()).toBeNull()
  })

  it('stop releases the microphone and resolves the decoded recording', async () => {
    const mic = await renderHook(() => useMicCapture())
    await act(() => mic.current.start())

    const clip = await mic.current.stop()

    expect(recorder?.state).toBe('inactive')
    expect(tracks.every((t) => t.stop.mock.calls.length === 1)).toBe(true)
    expect(close).toHaveBeenCalledTimes(1)
    expect(decodeMock.decodeAudio).toHaveBeenCalledTimes(1)
    expect(clip?.sampleRate).toBe(48000)
    expect(mic.current.liveFrame()).toBeNull()
  })

  it('stop resolves null when the recording cannot be decoded', async () => {
    decodeMock.decodeAudio.mockRejectedValue(new Error('EncodingError'))
    const mic = await renderHook(() => useMicCapture())
    await act(() => mic.current.start())

    await expect(mic.current.stop()).resolves.toBeNull()
    expect(close).toHaveBeenCalledTimes(1)
  })

  it('unmounting releases the microphone without decoding', async () => {
    const mic = await renderHook(() => useMicCapture())
    await act(() => mic.current.start())

    await unmountHook()

    expect(tracks.every((t) => t.stop.mock.calls.length === 1)).toBe(true)
    expect(close).toHaveBeenCalledTimes(1)
    expect(decodeMock.decodeAudio).not.toHaveBeenCalled()
  })

  it('unmounting while start() still awaits getUserMedia releases the late stream instead of leaking it', async () => {
    let resolveGetUserMedia: (mediaStream: typeof stream) => void
    getUserMedia.mockReset().mockReturnValue(
      new Promise((resolve) => {
        resolveGetUserMedia = resolve
      }),
    )
    const mic = await renderHook(() => useMicCapture())

    const started = mic.current.start()
    await unmountHook()
    const closeCallsAfterUnmount = close.mock.calls.length

    await act(async () => {
      resolveGetUserMedia(stream)
      await started
    })

    expect(tracks.every((t) => t.stop.mock.calls.length === 1)).toBe(true)
    expect(close).toHaveBeenCalledTimes(closeCallsAfterUnmount)
    expect(recorder).toBeNull()
  })
})
