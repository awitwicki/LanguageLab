import { useEffect, useRef, useState, type ReactNode } from 'react'
import { buildLut, parseColor, type Rgb } from './audio/colormap'
import { columnToRows, rowFrequencies, toImage, type Spectrogram } from './audio/spectrogram'
import type { LiveFrame } from './audio/useMicCapture'
import './SpectrogramStrip.css'

interface Props {
  label: string
  placeholder: string
  spectrogram: Spectrogram | null
  duration: number | null
  /** Polled every frame while set: the strip scrolls one column per frame. */
  live?: () => LiveFrame | null
  /** Polled every frame while set: a number 0–1 draws a line there, null draws none. */
  playhead?: () => number | null
  action?: ReactNode
}

const LIVE_COLUMN_PX = 2
const FALLBACK: { silence: Rgb; mid: Rgb; peak: Rgb; line: Rgb } = {
  silence: [232, 232, 237],
  mid: [0, 113, 227],
  peak: [29, 29, 31],
  line: [255, 59, 48],
}

function themeColors() {
  const style = getComputedStyle(document.documentElement)
  const read = (token: string, fallback: Rgb) => parseColor(style.getPropertyValue(token)) ?? fallback
  return {
    lut: buildLut([read('--surface-2', FALLBACK.silence), read('--accent', FALLBACK.mid), read('--text', FALLBACK.peak)]),
    line: read('--danger', FALLBACK.line),
  }
}

function paintImage(ctx: CanvasRenderingContext2D, image: Uint8ClampedArray, width: number, height: number, lut: Uint8ClampedArray) {
  const pixels = ctx.createImageData(width, height)
  for (let i = 0; i < image.length; i++) {
    const v = image[i] * 3
    pixels.data[i * 4] = lut[v]
    pixels.data[i * 4 + 1] = lut[v + 1]
    pixels.data[i * 4 + 2] = lut[v + 2]
    pixels.data[i * 4 + 3] = 255
  }
  ctx.putImageData(pixels, 0, 0)
}

function paintLine(ctx: CanvasRenderingContext2D, fraction: number, width: number, height: number, color: Rgb) {
  ctx.fillStyle = `rgb(${color[0]}, ${color[1]}, ${color[2]})`
  ctx.fillRect(Math.round(fraction * (width - 2)), 0, 2, height)
}

function useCanvasSize(canvas: { current: HTMLCanvasElement | null }, ready: boolean) {
  const [size, setSize] = useState(0)
  useEffect(() => {
    const element = canvas.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() => setSize((n) => n + 1))
    observer.observe(element)
    return () => observer.disconnect()
  }, [canvas, ready])
  return size
}

export function SpectrogramStrip({ label, placeholder, spectrogram, duration, live, playhead, action }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const showCanvas = spectrogram !== null || live !== undefined
  const resized = useCanvasSize(canvasRef, showCanvas)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const dpr = window.devicePixelRatio || 1
    const width = Math.max(1, Math.round((canvas.clientWidth || 300) * dpr))
    const height = Math.max(1, Math.round((canvas.clientHeight || 96) * dpr))
    canvas.width = width
    canvas.height = height
    const colors = themeColors()
    const rowHz = rowFrequencies(height)

    const image = spectrogram ? toImage(spectrogram, { width, height }) : null
    if (image) paintImage(ctx, image, width, height, colors.lut)
    else {
      ctx.fillStyle = `rgb(${colors.lut[0]}, ${colors.lut[1]}, ${colors.lut[2]})`
      ctx.fillRect(0, 0, width, height)
    }

    if (!live && !playhead) return

    let frame = 0
    let lineShown = false
    const column = ctx.createImageData(LIVE_COLUMN_PX, height)

    const tick = () => {
      if (live) {
        const current = live()
        if (current) {
          const rows = columnToRows(current.bins, current.binHz, rowHz)
          for (let y = 0; y < height; y++) {
            const v = Math.round(rows[y]) * 3
            for (let x = 0; x < LIVE_COLUMN_PX; x++) {
              const p = (y * LIVE_COLUMN_PX + x) * 4
              column.data[p] = colors.lut[v]
              column.data[p + 1] = colors.lut[v + 1]
              column.data[p + 2] = colors.lut[v + 2]
              column.data[p + 3] = 255
            }
          }
          ctx.drawImage(canvas, -LIVE_COLUMN_PX, 0)
          ctx.putImageData(column, width - LIVE_COLUMN_PX, 0)
        }
      }
      if (playhead && image) {
        const fraction = playhead()
        if (fraction !== null) {
          paintImage(ctx, image, width, height, colors.lut)
          paintLine(ctx, fraction, width, height, colors.line)
          lineShown = true
        } else if (lineShown) {
          paintImage(ctx, image, width, height, colors.lut)
          lineShown = false
        }
      }
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [spectrogram, live, playhead, resized, showCanvas])

  return (
    <div className="spectrogram-strip">
      <div className="spectrogram-strip-head">
        <span className="spectrogram-strip-label">{label}</span>
        {action}
      </div>
      {showCanvas ? (
        <canvas ref={canvasRef} className="spectrogram-strip-canvas" aria-label={`${label} spectrogram`} />
      ) : (
        <div className="spectrogram-strip-placeholder">{placeholder}</div>
      )}
      {duration !== null && <p className="spectrogram-strip-duration num">{duration.toFixed(1)} s</p>}
    </div>
  )
}
