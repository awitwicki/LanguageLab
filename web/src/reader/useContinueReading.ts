import { useEffect, useState } from 'react'
import { api, type ReaderBookDto } from '../api/client'
import type { BookStore } from './bookStore'

/** One book to pick up again, and whether this browser holds the file to do it with. */
export interface ContinueReading {
  book: ReaderBookDto
  /** true: the file is in this device's store, so the reader opens it straight away. */
  onDevice: boolean
}

/**
 * The books a "Reading" row on the home screen can lead back to — at most two, newest first,
 * and usually one.
 *
 * Both halves are needed. The server knows where the reader stopped in every book, in order of
 * when it last moved, but not which files this browser holds — the file never leaves the
 * browser that opened it. So the first row is always where the reader last was, wherever that
 * was; when that file is on another device (`onDevice` false) it can only be reached through
 * the library, which asks for the file, and the newest book this device *can* open follows it
 * so that the one tap straight into a book is still on the screen.
 *
 * Empty while the store is still opening, and on any failure: these are shortcuts, and a
 * missing one costs the user nothing but a detour through the library.
 */
export function useContinueReading(store: BookStore | null): ContinueReading[] {
  const [reading, setReading] = useState<ContinueReading[]>([])

  useEffect(() => {
    if (!store) {
      return
    }

    let cancelled = false

    Promise.all([api.listReaderBooks(), store.list()])
      .then(([library, onDevice]) => {
        // The library arrives newest-first (ReaderBookService orders by UpdatedAt), so the
        // first entry is wherever the reader last was.
        const newest = library[0]

        if (!newest) {
          return
        }

        const here = new Set(onDevice.map((meta) => meta.hash))
        const rows = [{ book: newest, onDevice: here.has(newest.fileHash) }]

        if (!rows[0].onDevice) {
          const present = library.find((candidate) => here.has(candidate.fileHash))

          if (present) rows.push({ book: present, onDevice: true })
        }

        if (!cancelled) setReading(rows)
      })
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [store])

  return reading
}
