import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { BookStore } from './bookStore'
import type { SentenceTranslation } from './Sentence'

const MESSAGES = {
  limit: 'Daily sentence translation limit reached',
  quota: 'Sentence translation is out of quota this month',
  failed: "Couldn't translate",
} as const

/**
 * Open, closed and failed sentence translations of one book. A translation comes from the
 * device's cache first, from the server after — and is cached on the device, since the server
 * keeps none. toggle closes an open or loading one and (re)requests a closed or failed one.
 */
export function useSentenceTranslations(hash: string, store: BookStore) {
  const [translations, setTranslations] = useState<Record<string, SentenceTranslation>>({})
  const current = useRef(translations)
  // Bumped on every state change for a key, so an in-flight request that started before a close
  // (or a re-toggle) can tell it was superseded and must not overwrite what happened meanwhile.
  const epoch = useRef<Record<string, number>>({})

  useEffect(() => {
    current.current = translations
  }, [translations])

  const set = useCallback((key: string, value: SentenceTranslation | undefined) => {
    epoch.current[key] = (epoch.current[key] ?? 0) + 1
    setTranslations((previous) => {
      const next = { ...previous }

      if (value) next[key] = value
      else delete next[key]

      current.current = next
      return next
    })
  }, [])

  const toggle = useCallback(
    async (key: string, text: string) => {
      const state = current.current[key]

      if (state && state.state !== 'error') {
        set(key, undefined)
        return
      }

      set(key, { state: 'loading' })
      const myEpoch = epoch.current[key]

      const cached = await store.getTranslation(hash, key).catch(() => null)

      if (myEpoch !== epoch.current[key]) return

      if (cached) {
        set(key, { state: 'open', text: cached })
        return
      }

      const result = await api.translateSentence(text)

      if (myEpoch !== epoch.current[key]) return

      if (result.status === 'ok') {
        set(key, { state: 'open', text: result.translation })
        void store.putTranslation(hash, key, result.translation).catch(() => undefined)
        return
      }

      set(key, { state: 'error', message: MESSAGES[result.status] })
    },
    [hash, store, set],
  )

  return { translations, toggle }
}
