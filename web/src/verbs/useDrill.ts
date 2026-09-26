import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type DrillCard, type DrillQuery, type VerbAnswerResult } from '../api/client'

/// What the server keeps at most (VerbScoring.MaxResponseMs). Clamping here as well keeps a
/// card left open for weeks from overflowing the int the request is typed with.
const MAX_RESPONSE_MS = 60_000

/**
 * One run of the drill: the card on screen, the answer once it is judged, and the card
 * behind it. The next card is fetched only after the answer has been posted — the server
 * picks by mastery, so asking earlier would pick on a row the answer has not reached yet.
 * The revealed answer gives that round trip all the time it needs.
 */
export function useDrill(query: DrillQuery) {
  const [card, setCard] = useState<DrillCard | null>(null)
  const [revealed, setRevealed] = useState<VerbAnswerResult | null>(null)
  const [peeked, setPeeked] = useState(false)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // 0 until the mount fetch's show() sets it — answer() is unreachable before card is set,
  // so it is never read at 0. Deferring the Date.now() call out of the render body avoids
  // calling an impure function on every render for a value React only ever keeps the first of.
  const shownAt = useRef(0)
  const queued = useRef<DrillCard | null>(null)
  // The server has no card identity to deduplicate against, so one card must post once.
  const answering = useRef(false)

  const show = useCallback((next: DrillCard | null) => {
    setCard(next)
    setDone(next === null)
    setPeeked(false)
    shownAt.current = Date.now()
  }, [])

  /// Uncovers the answer without judging the card: the learner still has to say whether
  /// they knew it. The clock keeps running, so a peeked "I know" scores as the slow answer
  /// it is rather than a free full mark.
  const peek = useCallback(() => {
    if (revealed) {
      return
    }

    setPeeked(true)
  }, [revealed])

  useEffect(() => {
    let live = true

    api
      .nextVerbCard(query)
      .then((first) => {
        if (live) {
          show(first)
        }
      })
      .catch((e) => live && setError(String(e)))

    return () => {
      live = false
    }
    // `query` is created once when the drill starts and kept in the route's state, so it
    // is referentially stable across renders — no need to pick it apart into primitives.
  }, [query, show])

  const answer = useCallback(
    (known: boolean) => {
      if (!card || answering.current || revealed) {
        return
      }

      answering.current = true
      setBusy(true)
      setError(null)
      const responseMs = Math.min(Math.max(0, Date.now() - shownAt.current), MAX_RESPONSE_MS)

      api
        .answerVerbCard({
          verb: card.verb.v1,
          promptForm: card.promptForm,
          known,
          responseMs,
          mode: query.mode,
          group: query.group,
        })
        .then(async (result) => {
          setRevealed(result)

          try {
            queued.current = await api.nextVerbCard({ ...query, exclude: card.verb.v1 })
          } catch {
            // A failed prefetch is not worth an error here: next() asks again.
            queued.current = null
          }
        })
        .catch((e) => {
          setError(String(e))
          // The card stays up for another try, so time it from here: the failed round trip
          // and the time spent reading the error are not the learner thinking about the verb.
          shownAt.current = Date.now()
        })
        .finally(() => {
          answering.current = false
          setBusy(false)
        })
    },
    [card, revealed, query],
  )

  const next = useCallback(() => {
    if (!revealed || answering.current) {
      return
    }

    const ready = queued.current
    queued.current = null

    if (ready) {
      setRevealed(null)
      show(ready)
      return
    }

    // Nothing was prefetched — a failed prefetch, or the stage just finished. Keep the
    // revealed answer on screen while fetching a fresh card instead of clearing it now:
    // clearing it here would reopen the verdict buttons on the card just answered, and
    // answer()'s own guard reads `revealed`, so leaving it set is what keeps a second
    // verdict (a keypress arriving before this fetch resolves) from posting again.
    const justAnswered = card?.verb.v1
    answering.current = true
    setBusy(true)
    api
      .nextVerbCard({ ...query, exclude: justAnswered })
      .then((fresh) => {
        setRevealed(null)
        show(fresh)
      })
      .catch((e) => setError(String(e)))
      .finally(() => {
        answering.current = false
        setBusy(false)
      })
  }, [revealed, query, show, card])

  return { card, revealed, peeked, done, busy, error, answer, peek, next }
}
