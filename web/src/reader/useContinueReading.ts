import { useEffect, useState } from 'react'
import { api, type ReaderBookDto } from '../api/client'
import type { BookStore } from './bookStore'

/**
 * The book a "Continue" button on the home screen can actually open: the most recently read
 * one whose file is on this device.
 *
 * Both halves are needed. The server knows where the reader stopped in every book, in order of
 * when it last moved, but not which files this browser holds — the file never leaves the
 * browser that opened it. So a book read on the phone is skipped here, and the next one down
 * that is present wins. null while the store is still opening, and on any failure: this is a
 * shortcut, and a missing one costs the user nothing but a detour through the library.
 */
export function useContinueReading(store: BookStore | null): ReaderBookDto | null {
  const [book, setBook] = useState<ReaderBookDto | null>(null)

  useEffect(() => {
    if (!store) {
      return
    }

    let cancelled = false

    Promise.all([api.listReaderBooks(), store.list()])
      .then(([library, onDevice]) => {
        const here = new Set(onDevice.map((meta) => meta.hash))

        // The library arrives newest-first (ReaderBookService orders by UpdatedAt), so the
        // first hit is the most recently read book this device can open.
        const found = library.find((candidate) => here.has(candidate.fileHash)) ?? null

        if (!cancelled) setBook(found)
      })
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [store])

  return book
}
