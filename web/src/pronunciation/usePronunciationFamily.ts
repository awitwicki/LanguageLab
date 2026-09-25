import { useCallback, useEffect, useState } from 'react'
import { api, type Accent, type PronunciationAttemptResult, type PronunciationWordDto } from '../api/client'

type Status = 'loading' | 'ready' | 'scoring' | 'feedback' | 'family-complete' | 'error'

interface State {
  status: Status
  error: string | null
  attemptError: string | null
  title: string
  word: PronunciationWordDto | null
  accent: Accent
  feedback: PronunciationAttemptResult | null
}

export interface UsePronunciationFamilyResult {
  /**
   * 'loading' also covers the gap between words: `word` still holds the previous one so
   * the screen can keep its card in place instead of flashing a placeholder.
   */
  status: Status
  /** A failure to load the family or its next word: fatal, nothing is on screen to practise. */
  error: string | null
  /**
   * A failure of one recording attempt — a denied microphone, a recognizer error, a
   * rejected POST — or of a reset. Kept apart from `error` because the word stays loaded
   * and usable: the screen shows this next to the Record button and the learner retries.
   */
  attemptError: string | null
  title: string
  word: PronunciationWordDto | null
  accent: Accent
  feedback: PronunciationAttemptResult | null
  setAccent: (accent: Accent) => void
  submitTranscript: (transcript: string) => Promise<void>
  /** Lets the screen report a recording failure that happens before any transcript exists. */
  reportAttemptError: (message: string | null) => void
  next: () => void
  practiceAgain: () => void
  /** Puts the word on screen back to New and serves the family again. */
  resetWord: () => Promise<void>
}

const ACCENT_KEY = 'pronunciation-accent'

// Browser storage is a boundary: it can be blocked or throw in private windows.
function readAccent(): Accent {
  try {
    return localStorage.getItem(ACCENT_KEY) === 'uk' ? 'uk' : 'us'
  } catch {
    return 'us'
  }
}

function storeAccent(accent: Accent) {
  try {
    localStorage.setItem(ACCENT_KEY, accent)
  } catch {
    // Nothing to do: the choice simply will not outlive this screen.
  }
}

const initialState: State = {
  status: 'loading',
  error: null,
  attemptError: null,
  title: '',
  word: null,
  accent: 'us',
  feedback: null,
}

export function usePronunciationFamily(familyKey: string): UsePronunciationFamilyResult {
  const [state, setState] = useState<State>(() => ({ ...initialState, accent: readAccent() }))

  const load = useCallback(
    (includeMastered: boolean) => {
      setState((s) => ({ ...s, status: 'loading', feedback: null, attemptError: null }))
      api
        .nextPronunciationWord(familyKey, includeMastered)
        .then((next) =>
          setState((s) => ({
            ...s,
            status: next.word ? 'ready' : 'family-complete',
            word: next.word,
            feedback: null,
            attemptError: null,
          })),
        )
        .catch((e) => setState((s) => ({ ...s, status: 'error', error: String(e) })))
    },
    [familyKey],
  )

  useEffect(() => {
    setState((s) => ({ ...initialState, accent: s.accent }))
    api
      .getPronunciationFamily(familyKey)
      .then((family) => setState((s) => ({ ...s, title: family.title })))
      .catch((e) => setState((s) => ({ ...s, status: 'error', error: String(e) })))
    load(false)
  }, [familyKey, load])

  const setAccent = useCallback((accent: Accent) => {
    storeAccent(accent)
    setState((s) => ({ ...s, accent }))
  }, [])

  const reportAttemptError = useCallback((message: string | null) => {
    setState((s) => ({ ...s, attemptError: message }))
  }, [])

  const submitTranscript = useCallback(
    async (transcript: string) => {
      const word = state.word
      if (state.status !== 'ready' || !word) {
        return
      }
      setState((s) => ({ ...s, status: 'scoring', attemptError: null }))
      try {
        const result = await api.submitPronunciationAttempt(word.word, state.accent, transcript)
        setState((s) => ({ ...s, status: 'feedback', feedback: result, attemptError: null }))
      } catch {
        // request() throws on any non-2xx, and the screen calls this as a floating
        // promise — without this the attempt would vanish into an unhandled rejection
        // and the screen would sit on 'ready' showing nothing at all.
        setState((s) => ({
          ...s,
          status: 'ready',
          feedback: null,
          attemptError: "Couldn't score that attempt — check your connection and try again.",
        }))
      }
    },
    [state.status, state.word, state.accent],
  )

  const resetWord = useCallback(async () => {
    const word = state.word
    if (!word) {
      return
    }

    try {
      await api.resetPronunciationWord(word.word)
    } catch {
      setState((s) => ({
        ...s,
        attemptError: "Couldn't reset that word — check your connection and try again.",
      }))
      return
    }

    load(false)
  }, [state.word, load])

  const next = useCallback(() => load(false), [load])
  const practiceAgain = useCallback(() => load(true), [load])

  return {
    status: state.status,
    error: state.error,
    attemptError: state.attemptError,
    title: state.title,
    word: state.word,
    accent: state.accent,
    feedback: state.feedback,
    setAccent,
    submitTranscript,
    reportAttemptError,
    next,
    practiceAgain,
    resetWord,
  }
}
