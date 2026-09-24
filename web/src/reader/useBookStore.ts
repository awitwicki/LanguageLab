import { useEffect, useState } from 'react'
import { MemoryBookStore, openIndexedDbBookStore, type BookStore } from './bookStore'

export interface BookStoreState {
  store: BookStore
  /** false: IndexedDB is unavailable and books live only until the tab closes. */
  persistent: boolean
}

/** Opened once for the app, so the library and the reader share the same store (and the memory fallback). */
export function useBookStore(): BookStoreState | null {
  const [state, setState] = useState<BookStoreState | null>(null)

  useEffect(() => {
    let cancelled = false

    openIndexedDbBookStore()
      .then((store) => {
        // Asks the browser not to evict the books under storage pressure (iOS, Telegram's web view).
        void navigator.storage?.persist?.().catch(() => undefined)

        if (!cancelled) {
          setState({ store, persistent: true })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ store: new MemoryBookStore(), persistent: false })
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  return state
}
