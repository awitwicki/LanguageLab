import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearClipCache, decodeAudio, decodeClip, isAudioSupported } from './decodeClip'

function fakeBuffer(channels: Float32Array[], sampleRate = 44100) {
  return {
    sampleRate,
    numberOfChannels: channels.length,
    length: channels[0].length,
    getChannelData: (i: number) => channels[i],
  }
}

const decodeAudioData = vi.fn()
const fetchMock = vi.fn()

beforeEach(() => {
  clearClipCache()
  decodeAudioData.mockReset().mockResolvedValue(fakeBuffer([new Float32Array([1, 1]), new Float32Array([0, 0.5])]))
  fetchMock.mockReset().mockResolvedValue({ ok: true, arrayBuffer: () => Promise.resolve(new ArrayBuffer(8)) })
  vi.stubGlobal('OfflineAudioContext', class { decodeAudioData = decodeAudioData })
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => vi.unstubAllGlobals())

describe('isAudioSupported', () => {
  it('needs OfflineAudioContext', () => {
    expect(isAudioSupported()).toBe(true)
    vi.stubGlobal('OfflineAudioContext', undefined)
    expect(isAudioSupported()).toBe(false)
  })
})

describe('decodeAudio', () => {
  it('averages the channels into mono and keeps the sample rate', async () => {
    const clip = await decodeAudio(new ArrayBuffer(8))

    expect(Array.from(clip.samples)).toEqual([0.5, 0.75])
    expect(clip.sampleRate).toBe(44100)
  })
})

describe('decodeClip', () => {
  it('fetches and decodes a URL once, then serves the cache', async () => {
    const first = await decodeClip('/pronunciation-audio/ship-us.ogg')
    const second = await decodeClip('/pronunciation-audio/ship-us.ogg')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith('/pronunciation-audio/ship-us.ogg')
    expect(second).toBe(first)
  })

  it('rejects on a failed fetch and does not cache the failure', async () => {
    fetchMock.mockResolvedValueOnce({ ok: false, status: 404, statusText: 'Not Found' })

    await expect(decodeClip('/missing.ogg')).rejects.toThrow('404')
    await expect(decodeClip('/missing.ogg')).resolves.toBeTruthy()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
