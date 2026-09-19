import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useClipAnalysis, type ClipView } from './useClipAnalysis'

const decodeMock = vi.hoisted(() => ({ decodeClip: vi.fn(), isAudioSupported: vi.fn(() => true) }))
vi.mock('./decodeClip', () => decodeMock)
;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

const RATE = 16000
const tone = Float32Array.from({ length: RATE }, (_, i) => 0.5 * Math.sin((2 * Math.PI * 440 * i) / RATE))

// The hook call is passed in as an argument, rather than named directly inside Probe's
// body, so the linter reads Probe as a plain test probe rather than a real component
// whose render must stay pure.
async function renderProbe(hook: () => ClipView | null) {
  const result = { current: null as ClipView | null }
  function Probe() {
    result.current = hook()
    return null
  }
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)
  await act(async () => root.render(createElement(Probe)))
  return { result, rerender: () => root.render(createElement(Probe)) }
}

async function renderHook(initial: string | null) {
  let url = initial
  const { result, rerender } = await renderProbe(() => useClipAnalysis(url))
  return {
    get current() {
      return result.current
    },
    setUrl(next: string | null) {
      url = next
      rerender()
    },
  }
}

async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0))
  })
}

beforeEach(() => {
  decodeMock.decodeClip.mockReset().mockResolvedValue({ samples: tone, sampleRate: RATE })
  decodeMock.isAudioSupported.mockReturnValue(true)
})

describe('useClipAnalysis', () => {
  it('is null without a url', async () => {
    const hook = await renderHook(null)

    expect(hook.current).toBeNull()
    expect(decodeMock.decodeClip).not.toHaveBeenCalled()
  })

  it('loads, then analyzes the clip', async () => {
    // A pre-resolved mock promise would settle within the same renderHook() act() call
    // (React drains any microtask-queued work before returning), leaving no observable
    // "loading" moment — so this holds the decode pending until asserted.
    let resolveDecode: (clip: { samples: Float32Array; sampleRate: number }) => void = () => {}
    decodeMock.decodeClip.mockReturnValueOnce(new Promise((resolve) => (resolveDecode = resolve)))
    const hook = await renderHook('/a.ogg')

    expect(hook.current?.status).toBe('loading')

    await act(async () => {
      resolveDecode({ samples: tone, sampleRate: RATE })
    })
    await settle()

    expect(hook.current?.status).toBe('ready')
    if (hook.current?.status === 'ready') expect(hook.current.analysis.duration).toBeCloseTo(1, 1)
  })

  it('reports a failed decode', async () => {
    decodeMock.decodeClip.mockRejectedValue(new Error('EncodingError'))
    const hook = await renderHook('/a.ogg')
    await settle()

    expect(hook.current?.status).toBe('failed')
  })

  it('reports unsupported when audio cannot be decoded here, without fetching', async () => {
    decodeMock.isAudioSupported.mockReturnValue(false)
    const hook = await renderHook('/a.ogg')
    await settle()

    expect(hook.current?.status).toBe('unsupported')
    expect(decodeMock.decodeClip).not.toHaveBeenCalled()
  })

  it('drops a result that arrives after the url changed', async () => {
    let resolveFirst: (clip: { samples: Float32Array; sampleRate: number }) => void = () => {}
    decodeMock.decodeClip
      .mockReturnValueOnce(new Promise((resolve) => (resolveFirst = resolve)))
      .mockResolvedValueOnce({ samples: tone.subarray(0, RATE / 2), sampleRate: RATE })
    const hook = await renderHook('/a.ogg')

    await act(async () => hook.setUrl('/b.ogg'))
    await settle()
    await act(async () => {
      resolveFirst({ samples: tone, sampleRate: RATE })
    })
    await settle()

    expect(hook.current?.status).toBe('ready')
    if (hook.current?.status === 'ready') expect(hook.current.analysis.duration).toBeCloseTo(0.5, 1)
  })
})
