import { describe, expect, it } from 'vitest'
import { analyzeClip, columnToRows, rowFrequencies, stft, toImage, trimSilence } from './spectrogram'

const RATE = 16000

function sine(freqHz: number, seconds: number, amplitude = 0.5): Float32Array {
  const out = new Float32Array(Math.round(seconds * RATE))
  for (let i = 0; i < out.length; i++) out[i] = amplitude * Math.sin((2 * Math.PI * freqHz * i) / RATE)
  return out
}

function concat(...parts: Float32Array[]): Float32Array {
  const out = new Float32Array(parts.reduce((n, p) => n + p.length, 0))
  let offset = 0
  for (const p of parts) {
    out.set(p, offset)
    offset += p.length
  }
  return out
}

describe('stft', () => {
  it('puts a 1 kHz tone in the 1 kHz bin and nowhere near 4 kHz', () => {
    const spec = stft(sine(1000, 0.5), RATE, { fftSize: 1024, hop: 256 })

    expect(spec.binHz).toBeCloseTo(15.625)
    expect(spec.hopSeconds).toBeCloseTo(0.016)
    const column = spec.columns[3]
    const loudest = column.indexOf(Math.max(...column))
    expect(Math.abs(loudest * spec.binHz - 1000)).toBeLessThan(spec.binHz)
    expect(column[loudest] - column[Math.round(4000 / spec.binHz)]).toBeGreaterThan(40)
  })

  it('yields one column per hop', () => {
    const spec = stft(new Float32Array(1024 + 256 * 9), RATE, { fftSize: 1024, hop: 256 })

    expect(spec.columns).toHaveLength(10)
    expect(spec.columns[0]).toHaveLength(512)
  })

  it('still yields one column for a clip shorter than a window', () => {
    const spec = stft(new Float32Array(100), RATE, { fftSize: 1024, hop: 256 })

    expect(spec.columns).toHaveLength(1)
  })

  it('never goes below the −100 dB floor on silence', () => {
    const spec = stft(new Float32Array(2048), RATE)

    expect(Math.min(...spec.columns[0])).toBe(-100)
  })
})

describe('trimSilence', () => {
  it('returns the span of the sound between two silences, padded by 50 ms', () => {
    const clip = concat(new Float32Array(RATE), sine(440, 0.3), new Float32Array(RATE))

    const { start, end } = trimSilence(clip, RATE)

    expect(start).toBeGreaterThanOrEqual(RATE - 0.06 * RATE)
    expect(start).toBeLessThanOrEqual(RATE - 0.04 * RATE)
    expect(end).toBeGreaterThanOrEqual(RATE * 1.3 + 0.04 * RATE)
    expect(end).toBeLessThanOrEqual(RATE * 1.3 + 0.06 * RATE)
  })

  it('leaves a clip alone when its sound is shorter than 100 ms', () => {
    const clip = concat(new Float32Array(RATE), sine(440, 0.05), new Float32Array(RATE))

    expect(trimSilence(clip, RATE)).toEqual({ start: 0, end: clip.length })
  })

  it('leaves pure silence alone', () => {
    const clip = new Float32Array(RATE)

    expect(trimSilence(clip, RATE)).toEqual({ start: 0, end: clip.length })
  })

  it('clamps the padding to the clip', () => {
    const clip = sine(440, 0.3)

    expect(trimSilence(clip, RATE)).toEqual({ start: 0, end: clip.length })
  })
})

describe('analyzeClip', () => {
  it('reports the trimmed duration and where the trim starts', () => {
    const clip = concat(new Float32Array(RATE), sine(440, 0.3), new Float32Array(RATE))

    const analysis = analyzeClip(clip, RATE)

    expect(analysis.duration).toBeCloseTo(0.4, 1)
    expect(analysis.trimStart).toBeCloseTo(0.95, 1)
    expect(analysis.spectrogram.columns.length).toBeGreaterThan(10)
  })
})

describe('rowFrequencies', () => {
  it('runs from maxHz at the top to minHz at the bottom on a log scale', () => {
    const rows = rowFrequencies(3, { minHz: 100, maxHz: 8000 })

    expect(rows[0]).toBeCloseTo(8000, 0)
    expect(rows[1]).toBeCloseTo(Math.sqrt(100 * 8000), 0)
    expect(rows[2]).toBeCloseTo(100, 0)
  })
})

describe('columnToRows', () => {
  it('takes the loudest bin within each row band', () => {
    // Bins centred at 0, 1000, 2000, 3000 Hz. The top row's band runs from the geometric
    // midpoint with the row below (≈1323 Hz) upwards, so it spans bins 2–3; the bottom
    // row's band (≈71–1323 Hz) spans bins 0–1.
    const column = [10, 20, 30, 40]
    const rows = columnToRows(column, 1000, new Float32Array([3500, 500]))

    expect(Array.from(rows)).toEqual([40, 20])
  })

  it('falls back to the nearest bin when a row spans no bin centre', () => {
    const column = [0, 50]
    const rows = columnToRows(column, 1000, new Float32Array([1400, 1300, 1200]))

    expect(Array.from(rows)).toEqual([50, 50, 50])
  })
})

describe('toImage', () => {
  it('produces width × height intensities with the loudest spot at 255', () => {
    const spec = stft(sine(1000, 0.5), RATE)

    const image = toImage(spec, { width: 20, height: 12 })

    expect(image).toHaveLength(240)
    expect(Math.max(...image)).toBe(255)
  })

  it('lights the row nearest 1 kHz for a 1 kHz tone, not the top row', () => {
    const spec = stft(sine(1000, 0.5), RATE)
    const height = 40
    const image = toImage(spec, { width: 4, height })
    const rowHz = rowFrequencies(height)
    const toneRow = [...rowHz].findIndex((hz) => hz <= 1000)

    // The 1 kHz bin falls in this row or the one just above it, depending on where the
    // band boundary lands; the Hann main lobe lights both.
    expect(Math.max(image[toneRow * 4 + 1], image[(toneRow - 1) * 4 + 1])).toBeGreaterThan(200)
    expect(image[1]).toBeLessThan(40)
  })

  it('is all zero for a spectrogram with no columns', () => {
    expect(Array.from(toImage({ columns: [], binHz: 10, hopSeconds: 0.01 }, { width: 3, height: 2 }))).toEqual([0, 0, 0, 0, 0, 0])
  })
})
