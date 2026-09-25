import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from '../test/render'
import { stft } from './audio/spectrogram'
import { SpectrogramStrip } from './SpectrogramStrip'

const putImageData = vi.fn()
const fillRect = vi.fn()
const drawImage = vi.fn()

function fakeContext() {
  return {
    putImageData,
    fillRect,
    drawImage,
    createImageData: (w: number, h: number) => ({ data: new Uint8ClampedArray(w * h * 4), width: w, height: h }),
    fillStyle: '',
  } as unknown as CanvasRenderingContext2D
}

beforeEach(() => {
  putImageData.mockClear()
  fillRect.mockClear()
  drawImage.mockClear()
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(() => fakeContext())
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

const tone = stft(
  Float32Array.from({ length: 8000 }, (_, i) => Math.sin((2 * Math.PI * 440 * i) / 16000)),
  16000,
)

describe('SpectrogramStrip', () => {
  it('shows the placeholder when there is nothing to draw', async () => {
    const { container } = await render(<SpectrogramStrip label="Your attempt" placeholder="Press Record" spectrogram={null} duration={null} />)

    expect(container.querySelector('.spectrogram-strip-label')?.textContent).toBe('Your attempt')
    expect(container.querySelector('.spectrogram-strip-placeholder')?.textContent).toBe('Press Record')
    expect(container.querySelector('canvas')).toBeNull()
  })

  it('paints a spectrogram onto a canvas and shows its duration', async () => {
    const { container } = await render(<SpectrogramStrip label="Reference" placeholder="Loading…" spectrogram={tone} duration={0.73} />)

    expect(container.querySelector('canvas')).not.toBeNull()
    expect(container.querySelector('.spectrogram-strip-placeholder')).toBeNull()
    expect(container.querySelector('.spectrogram-strip-duration')?.textContent).toBe('0.7 s')
    expect(putImageData).toHaveBeenCalled()
  })

  it('renders the action beside the label', async () => {
    const { container } = await render(
      <SpectrogramStrip label="Reference" placeholder="" spectrogram={null} duration={null} action={<button type="button">Play</button>} />,
    )

    expect(container.querySelector('.spectrogram-strip-head button')?.textContent).toBe('Play')
  })

  it('in live mode scrolls a column in on every frame', async () => {
    const live = vi.fn(() => ({ bins: new Uint8Array(512).fill(200), binHz: 46.875 }))
    const { container } = await render(<SpectrogramStrip label="Your attempt" placeholder="Press Record" spectrogram={null} duration={null} live={live} />)

    await act(async () => {
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))
    })

    expect(container.querySelector('canvas')).not.toBeNull()
    expect(live).toHaveBeenCalled()
    expect(drawImage).toHaveBeenCalled()
    expect(putImageData).toHaveBeenCalled()
  })

  it('stops polling once unmounted', async () => {
    const live = vi.fn(() => null)
    const { unmount } = await render(<SpectrogramStrip label="x" placeholder="" spectrogram={null} duration={null} live={live} />)
    await act(async () => {
      await new Promise((resolve) => requestAnimationFrame(resolve))
    })
    await unmount()
    const calls = live.mock.calls.length

    await act(async () => {
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))
    })

    expect(live.mock.calls.length).toBe(calls)
  })

  it('attaches the ResizeObserver once the canvas appears after starting in placeholder mode', async () => {
    const observeSpy = vi.fn()
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe = observeSpy
        disconnect() {}
      },
    )

    const { container, rerender } = await render(
      <SpectrogramStrip label="Your attempt" placeholder="Press Record" spectrogram={null} duration={null} />,
    )

    expect(container.querySelector('canvas')).toBeNull()
    expect(observeSpy).not.toHaveBeenCalled()

    await rerender(<SpectrogramStrip label="Your attempt" placeholder="Press Record" spectrogram={tone} duration={0.73} />)

    expect(container.querySelector('canvas')).not.toBeNull()
    expect(observeSpy).toHaveBeenCalled()
  })
})
