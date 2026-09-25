export interface Spectrogram {
  /** One entry per hop; each holds fftSize / 2 magnitudes in dB, floored at DB_FLOOR. */
  columns: Float32Array[]
  binHz: number
  hopSeconds: number
}

export interface StftOptions {
  fftSize?: number
  hop?: number
}

export interface SampleRange {
  start: number
  end: number
}

export interface ClipAnalysis {
  spectrogram: Spectrogram
  /** Seconds of sound kept after trimming. */
  duration: number
  /** Seconds into the original clip where the kept part starts. */
  trimStart: number
}

export const DB_FLOOR = -100

const SILENCE_FRAME_SECONDS = 0.02
const SILENCE_PAD_SECONDS = 0.05
const SILENCE_THRESHOLD_DB = 40
const MIN_TRIMMED_SECONDS = 0.1

function hann(n: number): Float32Array {
  const window = new Float32Array(n)
  for (let i = 0; i < n; i++) window[i] = 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / (n - 1))
  return window
}

/** In-place iterative radix-2 FFT; both arrays must share one power-of-two length. */
function fft(re: Float32Array, im: Float32Array): void {
  const n = re.length
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1
    for (; j & bit; bit >>= 1) j ^= bit
    j ^= bit
    if (i < j) {
      ;[re[i], re[j]] = [re[j], re[i]]
      ;[im[i], im[j]] = [im[j], im[i]]
    }
  }
  for (let len = 2; len <= n; len <<= 1) {
    const angle = (-2 * Math.PI) / len
    const wRe = Math.cos(angle)
    const wIm = Math.sin(angle)
    const half = len >> 1
    for (let i = 0; i < n; i += len) {
      let cRe = 1
      let cIm = 0
      for (let k = 0; k < half; k++) {
        const aRe = re[i + k]
        const aIm = im[i + k]
        const bRe = re[i + k + half] * cRe - im[i + k + half] * cIm
        const bIm = re[i + k + half] * cIm + im[i + k + half] * cRe
        re[i + k] = aRe + bRe
        im[i + k] = aIm + bIm
        re[i + k + half] = aRe - bRe
        im[i + k + half] = aIm - bIm
        const nextRe = cRe * wRe - cIm * wIm
        cIm = cRe * wIm + cIm * wRe
        cRe = nextRe
      }
    }
  }
}

export function stft(samples: Float32Array, sampleRate: number, { fftSize = 1024, hop = 256 }: StftOptions = {}): Spectrogram {
  const input = samples.length >= fftSize ? samples : padTo(samples, fftSize)
  const window = hann(fftSize)
  const bins = fftSize / 2
  // A full-scale sine under a Hann window peaks at amplitude · fftSize / 4.
  const scale = 4 / fftSize
  const re = new Float32Array(fftSize)
  const im = new Float32Array(fftSize)
  const columns: Float32Array[] = []

  for (let start = 0; start + fftSize <= input.length; start += hop) {
    for (let i = 0; i < fftSize; i++) {
      re[i] = input[start + i] * window[i]
      im[i] = 0
    }
    fft(re, im)
    const column = new Float32Array(bins)
    for (let b = 0; b < bins; b++) {
      const magnitude = Math.hypot(re[b], im[b]) * scale
      column[b] = Math.max(DB_FLOOR, 20 * Math.log10(magnitude + 1e-12))
    }
    columns.push(column)
  }

  return { columns, binHz: sampleRate / fftSize, hopSeconds: hop / sampleRate }
}

function padTo(samples: Float32Array, length: number): Float32Array {
  const padded = new Float32Array(length)
  padded.set(samples)
  return padded
}

export function trimSilence(samples: Float32Array, sampleRate: number): SampleRange {
  const whole = { start: 0, end: samples.length }
  const frame = Math.max(1, Math.round(sampleRate * SILENCE_FRAME_SECONDS))
  const frames = Math.ceil(samples.length / frame)
  const rms = new Float32Array(frames)
  let peak = 0

  for (let f = 0; f < frames; f++) {
    const from = f * frame
    const to = Math.min(samples.length, from + frame)
    let sum = 0
    for (let i = from; i < to; i++) sum += samples[i] * samples[i]
    rms[f] = Math.sqrt(sum / Math.max(1, to - from))
    if (rms[f] > peak) peak = rms[f]
  }

  if (peak === 0) return whole

  const threshold = peak / 10 ** (SILENCE_THRESHOLD_DB / 20)
  let first = 0
  while (first < frames && rms[first] < threshold) first++
  let last = frames - 1
  while (last > first && rms[last] < threshold) last--

  if ((last + 1 - first) * frame < sampleRate * MIN_TRIMMED_SECONDS) return whole

  const pad = Math.round(sampleRate * SILENCE_PAD_SECONDS)
  return {
    start: Math.max(0, first * frame - pad),
    end: Math.min(samples.length, (last + 1) * frame + pad),
  }
}

export function analyzeClip(samples: Float32Array, sampleRate: number): ClipAnalysis {
  const { start, end } = trimSilence(samples, sampleRate)
  return {
    spectrogram: stft(samples.subarray(start, end), sampleRate),
    duration: (end - start) / sampleRate,
    trimStart: start / sampleRate,
  }
}

export interface AxisOptions {
  minHz?: number
  maxHz?: number
}

export interface ImageOptions extends AxisOptions {
  width: number
  height: number
}

export const DB_RANGE = 60

export function rowFrequencies(height: number, { minHz = 100, maxHz = 8000 }: AxisOptions = {}): Float32Array {
  const rows = new Float32Array(height)
  const logMin = Math.log(minHz)
  const logMax = Math.log(maxHz)
  for (let r = 0; r < height; r++) {
    const t = height === 1 ? 1 : 1 - r / (height - 1)
    rows[r] = Math.exp(logMin + t * (logMax - logMin))
  }
  return rows
}

export function columnToRows(column: ArrayLike<number>, binHz: number, rowHz: Float32Array): Float32Array {
  const rows = new Float32Array(rowHz.length)
  const lastBin = column.length - 1
  for (let r = 0; r < rowHz.length; r++) {
    // A row spans from the geometric midpoint below it to the one above it.
    const above = r === 0 ? rowHz[0] * (rowHz[0] / rowHz[Math.min(1, rowHz.length - 1)]) : Math.sqrt(rowHz[r - 1] * rowHz[r])
    const below = r === rowHz.length - 1 ? rowHz[r] * (rowHz[r] / rowHz[Math.max(0, r - 1)]) : Math.sqrt(rowHz[r] * rowHz[r + 1])
    const lo = Math.ceil(Math.min(below, above) / binHz)
    const hi = Math.floor(Math.max(below, above) / binHz)
    if (hi < lo || lo > lastBin) {
      rows[r] = column[Math.max(0, Math.min(lastBin, Math.round(rowHz[r] / binHz)))]
      continue
    }
    let max = -Infinity
    for (let b = Math.max(0, lo); b <= Math.min(lastBin, hi); b++) if (column[b] > max) max = column[b]
    rows[r] = max
  }
  return rows
}

export function toImage(spec: Spectrogram, { width, height, minHz, maxHz }: ImageOptions): Uint8ClampedArray {
  const image = new Uint8ClampedArray(width * height)
  if (spec.columns.length === 0 || width === 0 || height === 0) return image

  const rowHz = rowFrequencies(height, { minHz, maxHz })
  const sampled: Float32Array[] = []
  let peak = DB_FLOOR
  for (let x = 0; x < width; x++) {
    const column = spec.columns[Math.min(spec.columns.length - 1, Math.floor((x / width) * spec.columns.length))]
    const rows = columnToRows(column, spec.binHz, rowHz)
    for (let y = 0; y < height; y++) if (rows[y] > peak) peak = rows[y]
    sampled.push(rows)
  }

  const floor = peak - DB_RANGE
  for (let x = 0; x < width; x++) {
    for (let y = 0; y < height; y++) {
      const t = (sampled[x][y] - floor) / DB_RANGE
      image[y * width + x] = Math.round(255 * Math.min(1, Math.max(0, t)))
    }
  }
  return image
}
