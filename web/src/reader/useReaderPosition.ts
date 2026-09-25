import { useCallback, useEffect, useRef } from 'react'
import { api, type ReaderBookDto } from '../api/client'
import { bookProgress, type ReaderBook, type ReaderPosition } from './readerBook'

/** How long scrolling has to stop before the position goes to the server. */
export const SAVE_DELAY_MS = 2000

export interface SavedPosition {
  position: ReaderPosition
  progress: number
  /** ISO 8601, this device's clock. */
  updatedAt: string
}

const storageKey = (hash: string) => `reader.position.${hash}`

export function loadLocalPosition(hash: string): SavedPosition | null {
  try {
    const raw = localStorage.getItem(storageKey(hash))
    return raw ? (JSON.parse(raw) as SavedPosition) : null
  } catch {
    return null
  }
}

function saveLocalPosition(hash: string, saved: SavedPosition) {
  try {
    localStorage.setItem(storageKey(hash), JSON.stringify(saved))
  } catch {
    // The server copy still goes out.
  }
}

/** The later of this device's own record and the server's wins. */
export function pickPosition(local: SavedPosition | null, server: ReaderBookDto | null): ReaderPosition | null {
  const fromServer = server
    ? { chapterIndex: server.chapterIndex, paragraphIndex: server.paragraphIndex, sentenceIndex: server.sentenceIndex }
    : null

  if (!local) return fromServer
  if (!server) return local.position

  return Date.parse(server.updatedAt) > Date.parse(local.updatedAt) ? fromServer : local.position
}

/**
 * Returns report(position): the position is kept on the device at once and sent to the server
 * once scrolling pauses for SAVE_DELAY_MS, or at once when the page is hidden. A failed send is
 * kept and goes out with the next one.
 */
export function useReaderPosition(hash: string, book: ReaderBook | null) {
  const pending = useRef<SavedPosition | null>(null)
  const timer = useRef<number | undefined>(undefined)

  const flush = useCallback(() => {
    window.clearTimeout(timer.current)
    const next = pending.current

    if (!next) return

    pending.current = null
    void api
      .saveReaderPosition(hash, { ...next.position, progress: next.progress, clientUpdatedAt: next.updatedAt })
      .catch(() => {
        pending.current ??= next
      })
  }, [hash])

  const report = useCallback(
    (position: ReaderPosition) => {
      if (!book) return

      const saved = { position, progress: bookProgress(book, position), updatedAt: new Date().toISOString() }
      saveLocalPosition(hash, saved)
      pending.current = saved
      window.clearTimeout(timer.current)
      timer.current = window.setTimeout(flush, SAVE_DELAY_MS)
    },
    [book, hash, flush],
  )

  useEffect(() => {
    const onVisibility = () => {
      if (document.visibilityState === 'hidden') flush()
    }

    document.addEventListener('visibilitychange', onVisibility)

    return () => {
      document.removeEventListener('visibilitychange', onVisibility)
      flush()
    }
  }, [flush])

  return report
}
