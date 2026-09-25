export interface Clip {
  samples: Float32Array
  sampleRate: number
}

const cache = new Map<string, Promise<Clip>>()

export function isAudioSupported(): boolean {
  return typeof OfflineAudioContext !== 'undefined'
}

function mixToMono(buffer: AudioBuffer): Float32Array {
  const mono = new Float32Array(buffer.length)
  for (let c = 0; c < buffer.numberOfChannels; c++) {
    const channel = buffer.getChannelData(c)
    for (let i = 0; i < mono.length; i++) mono[i] += channel[i] / buffer.numberOfChannels
  }
  return mono
}

// An offline context decodes without the autoplay policy's "not allowed to start" warning
// that a regular AudioContext logs when created outside a user gesture.
export async function decodeAudio(bytes: ArrayBuffer): Promise<Clip> {
  const buffer = await new OfflineAudioContext(1, 1, 44100).decodeAudioData(bytes)
  return { samples: mixToMono(buffer), sampleRate: buffer.sampleRate }
}

export function decodeClip(url: string): Promise<Clip> {
  let pending = cache.get(url)
  if (!pending) {
    pending = fetch(url)
      .then((response) => {
        if (!response.ok) throw new Error(`${response.status} ${response.statusText}`)
        return response.arrayBuffer()
      })
      .then(decodeAudio)
    pending.catch(() => cache.delete(url))
    cache.set(url, pending)
  }
  return pending
}

export function clearClipCache(): void {
  cache.clear()
}
