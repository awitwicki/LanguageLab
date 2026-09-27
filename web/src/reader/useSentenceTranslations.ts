import { useCallback, useEffect, useRef, useState } from 'react'
import { useLearnerLanguage } from '../account/learnerLanguage'
import { api } from '../api/client'
import type { BookStore } from './bookStore'
import type { SentenceTranslation } from './Sentence'

const MESSAGES = {
  limit: 'Daily sentence translation limit reached',
  quota: 'Translation limit reached — try again later',
  tooLong: 'This sentence is too long to translate',
  failed: "Couldn't translate",
} as const

/**
 * Open, closed and failed sentence translations of one book. A translation comes from the
 * device's cache first, from the server after — and is cached on the device, since the server
 * keeps none. toggle closes an open or loading one and (re)requests a closed or failed one.
 */
export function useSentenceTranslations(hash: string, store: BookStore) {
  // A cached translation is only valid for the language it was made in — the cache key below
  // includes it, so switching languages can never surface another language's cached text.
  const language = useLearnerLanguage()
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

      const cached = await store.getTranslation(hash, language, key).catch(() => null)

      if (myEpoch !== epoch.current[key]) return

      if (cached) {
        set(key, { state: 'open', text: cached })
        return
      }

      const result = await api.translateSentence(text)

      if (myEpoch !== epoch.current[key]) return

      if (result.status === 'ok') {
        set(key, { state: 'open', text: result.translation })
        void store.putTranslation(hash, language, key, result.translation).catch(() => undefined)
        return
      }

      set(key, { state: 'error', message: MESSAGES[result.status] })
    },
    [hash, store, language, set],
  )

  return { translations, toggle }
}
