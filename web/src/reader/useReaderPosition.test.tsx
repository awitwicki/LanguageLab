import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReaderBookDto } from '../api/client'
import { render } from '../test/render'
import { READER_BOOK_XML } from '../test/readerFixtures'
import { parseReaderBook, type ReaderPosition } from './readerBook'
import { loadLocalPosition, pickPosition, SAVE_DELAY_MS, useReaderPosition } from './useReaderPosition'

const apiMock = vi.hoisted(() => ({ saveReaderPosition: vi.fn() }))

vi.mock('../api/client', () => ({ api: apiMock }))

const book = parseReaderBook(READER_BOOK_XML, 'x')
const HASH = 'a'.repeat(64)
const at = (chapterIndex: number, paragraphIndex = 0, sentenceIndex = 0): ReaderPosition => ({
  chapterIndex,
  paragraphIndex,
  sentenceIndex,
})

let report: (position: ReaderPosition) => void = () => {}

function Probe() {
  report = useReaderPosition(HASH, book)
  return null
}

beforeEach(() => {
  vi.useFakeTimers()
  apiMock.saveReaderPosition.mockReset().mockResolvedValue(null)
  localStorage.clear()
})

afterEach(() => vi.useRealTimers())

describe('useReaderPosition', () => {
  it('keeps the position on the device at once and sends it after a pause', async () => {
    await render(<Probe />)

    act(() => report(at(1)))

    expect(loadLocalPosition(HASH)?.position).toEqual(at(1))
    expect(apiMock.saveReaderPosition).not.toHaveBeenCalled()

    await act(async () => vi.advanceTimersByTime(SAVE_DELAY_MS))

    expect(apiMock.saveReaderPosition).toHaveBeenCalledTimes(1)
    expect(apiMock.saveReaderPosition).toHaveBeenCalledWith(
      HASH,
      expect.objectContaining({ chapterIndex: 1, paragraphIndex: 0, sentenceIndex: 0, progress: 0.5 }),
    )
  })

  it('sends only the last of a burst of positions', async () => {
    await render(<Probe />)

    act(() => report(at(0, 0, 1)))
    act(() => report(at(0, 1, 0)))
    await act(async () => vi.advanceTimersByTime(SAVE_DELAY_MS))

    expect(apiMock.saveReaderPosition).toHaveBeenCalledTimes(1)
    expect(apiMock.saveReaderPosition.mock.calls[0][1]).toMatchObject({ paragraphIndex: 1 })
  })

  it('sends at once when the page is hidden', async () => {
    await render(<Probe />)

    act(() => report(at(1)))
    Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true })
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
    })
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })

    expect(apiMock.saveReaderPosition).toHaveBeenCalledTimes(1)
  })
})

describe('pickPosition', () => {
  const server = (updatedAt: string, chapterIndex: number) =>
    ({ chapterIndex, paragraphIndex: 0, sentenceIndex: 0, updatedAt }) as ReaderBookDto

  it('takes the later of the device and the server', () => {
    const local = { position: at(3), progress: 0.3, updatedAt: '2026-09-24T10:00:00.000Z' }

    expect(pickPosition(local, server('2026-09-24T11:00:00.000Z', 7))).toEqual(at(7))
    expect(pickPosition(local, server('2026-09-24T09:00:00.000Z', 7))).toEqual(at(3))
    expect(pickPosition(null, server('2026-09-24T09:00:00.000Z', 7))).toEqual(at(7))
    expect(pickPosition(null, null)).toBeNull()
  })
})
