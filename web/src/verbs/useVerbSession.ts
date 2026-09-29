import { useCallback, useEffect, useRef, useState } from 'react'
import { api, type DrillQuery, type SessionVerb, type VerbSession } from '../api/client'
import { createOutbox } from './outbox'
import { resolveWordCount } from './sessionSettings'
import { clearSavedSession, readSavedSession, writeSavedSession, type SavedSession } from './savedSession'
import {
  applyAnswer,
  createDrill,
  exerciseFor,
  isDone,
  PassStreak,
  type Card,
  type DrillState,
  type Played,
} from './session'

/// What the server keeps at most (VerbScoring.MaxResponseMs). Clamping here as well keeps a
/// card left open for weeks from overflowing the int the request is typed with.
const MaxResponseMs = 60_000

/// How few cards may be left of a free run's queue before the next chunk is fetched.
const RefillAt = 5

export type Phase = 'loading' | 'start' | 'drill' | 'done' | 'finished'

/** Who the round is saved for, and — after a reload — the saved round to pick up. */
export interface SessionSaving {
  userId: number
  title: string
  resume?: SavedSession | null
}

/**
 * One training session, run in the browser. The server hands over the words and their
 * standing; the choice of the next card happens here, so no click waits on the network.
 * Answers leave through the outbox in the background.
 *
 * Ordinary training opens on the start screen (`start`), plays the drill round (`drill`) and
 * ends on `done` — or on `finished` when the whole stage has already passed. A free run has
 * nothing to choose the words of and opens straight on `drill`.
 *
 * Given `saving`, the round is kept on the device as it is played, so a reload can hand it
 * back as `saving.resume` and carry on from the same card — see `savedSession.ts`.
 *
 * `wordCount` is the account's last choice (null before it has ever made one); `onWordCountSaved`
 * fires once a fresh choice, made by pressing Start, has reached the account.
 */
export function useVerbSession(
  query: DrillQuery,
  saving?: SessionSaving,
  wordCount?: number | null,
  onWordCountSaved?: () => void,
) {
  const [phase, setPhase] = useState<Phase>('loading')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [syncing, setSyncing] = useState(false)
  const [stuck, setStuck] = useState(false)
  const [offer, setOffer] = useState<SessionVerb[]>([])
  const [words, setWords] = useState(0)
  const [scope, setScope] = useState({ passed: 0, total: 0 })
  const [played, setPlayed] = useState<Played | null>(null)
  const [drill, setDrill] = useState<DrillState | null>(null)
  const [picked, setPicked] = useState<string | null>(null)

  const resume = saving?.resume ?? null
  // Answers a saved round never sent go out with this one, resumed or not: a fresh round
  // started over a saved one replaces it, and must not drop what it had not delivered.
  const [outbox] = useState(() =>
    createOutbox(api.postVerbAnswers, (resume ?? (saving ? readSavedSession(saving.userId) : null))?.pending),
  )
  // The latest `save`, for the outbox to reach: its queue changes outside any render.
  const saveRef = useRef(() => {})
  const discarded = useRef(false)
  const shownAt = useRef(0)
  // Which card `answer()` has already recorded — by reference, since `session.ts` builds a
  // fresh `Card` object for every deal, even a verb repeating from earlier in the round. Kept
  // as an identity rather than a boolean cleared inside `advance()`/`startedAt()`: those run
  // synchronously inside the very `answer()` call that is guarding against re-entrancy, so a
  // boolean would already be clear again before a second, same-tick call (a double click, or a
  // held key firing twice before React re-renders) had a chance to see it set.
  const answeredCard = useRef<Card | null>(null)
  // The drill state the revealed card will move to, held while the answer is on screen.
  const pending = useRef<DrillState | null>(null)
  const refilling = useRef(false)

  const startedAt = useCallback(() => {
    shownAt.current = Date.now()
    setPicked(null)
  }, [])

  const load = useCallback(
    async (): Promise<VerbSession | null> => {
      setBusy(true)

      try {
        const session = await api.getVerbSession(query)
        setError(null)

        return session
      } finally {
        setBusy(false)
      }
    },
    [query],
  )

  const open = useCallback(
    (session: VerbSession | null) => {
      if (session === null) {
        setPhase('finished')

        return
      }

      setScope(session.scope)

      if (session.queue) {
        setPlayed({ kind: 'queue', cards: cardsOf(session), index: 0 })
        setDrill(null)
        setPhase('drill')
        startedAt()

        return
      }

      setOffer(session.verbs)
      setWords(resolveWordCount(wordCount, session.verbs.length))
      setPlayed(null)
      setDrill(null)
      setPhase('start')
    },
    [startedAt, wordCount],
  )

  // One last push on the way out: a closing tab gets a single shot, which is why the endpoint
  // takes an array.
  useEffect(() => {
    const leave = () => {
      outbox.flush().catch(() => {})
    }

    window.addEventListener('pagehide', leave)
    document.addEventListener('visibilitychange', leave)

    return () => {
      window.removeEventListener('pagehide', leave)
      document.removeEventListener('visibilitychange', leave)
    }
  }, [outbox])

  const card = played ? (played.cards[played.index] ?? null) : (drill?.current ?? null)

  // Re-arms the guard only once React has actually committed a new card — the one moment a
  // same-tick double call cannot fake, unlike calling `advance()` itself.
  useEffect(() => {
    answeredCard.current = null
  }, [card])

  const beginDrill = useCallback(
    (chosenWords: SessionVerb[]) => {
      setPlayed(null)
      setDrill(createDrill(chosenWords))
      setPhase(chosenWords.length === 0 ? 'done' : 'drill')
      startedAt()
    },
    [startedAt],
  )

  const start = useCallback(() => {
    beginDrill(offer.slice(0, words))
    void api.setVerbsWordCount(words).then(onWordCountSaved).catch(() => {})
  }, [beginDrill, offer, words, onWordCountSaved])

  const finish = useCallback(() => {
    setPhase('done')
    setSyncing(true)
    outbox
      .flush()
      .catch(() => {})
      .finally(() => setSyncing(false))
  }, [outbox])

  /// A free run's queue is topped up before it empties, so the learner never waits on it.
  const refill = useCallback(() => {
    if (refilling.current) {
      return
    }

    refilling.current = true
    setBusy(true)

    api
      .getVerbSession(query)
      .then((session) => {
        if (session?.queue) {
          setPlayed((current) =>
            current ? { ...current, cards: [...current.cards, ...cardsOf(session)] } : current,
          )
        }
      })
      .catch((e) => setError(String(e)))
      .finally(() => {
        refilling.current = false
        setBusy(false)
      })
  }, [query])

  const advance = useCallback(() => {
    if (played) {
      const index = played.index + 1

      setPlayed({ ...played, index })
      startedAt()

      if (played.cards.length - index <= RefillAt) {
        refill()
      }

      return
    }

    const next = pending.current ?? drill

    pending.current = null

    if (next) {
      setDrill(next)
      startedAt()

      if (isDone(next)) {
        finish()
      }
    }
  }, [drill, finish, played, refill, startedAt])

  /// Puts a saved round back on screen. It was saved on the card the learner lands on next, so
  /// nothing here needs to know whether the last one was answered.
  const restore = useCallback(
    (saved: SavedSession) => {
      setScope(saved.scope)
      setOffer(saved.offer)
      setWords(saved.words)
      setPlayed(saved.played)
      setDrill(saved.drill)

      if (saved.played) {
        setPhase('drill')
        startedAt()

        if (saved.played.cards.length - saved.played.index <= RefillAt) {
          refill()
        }

        return
      }

      if (saved.drill && isDone(saved.drill)) {
        finish()

        return
      }

      setPhase('drill')
      startedAt()
    },
    [finish, refill, startedAt],
  )

  useEffect(() => {
    let live = true

    // Whatever a saved round left unsent goes out first, before anything new is answered.
    if (outbox.pending() > 0) {
      outbox
        .flush()
        .then(() => setStuck(false))
        .catch(() => setStuck(true))
    }

    if (resume) {
      // Put back a tick later, the way a fetched session lands, rather than inside the effect.
      void Promise.resolve(resume).then((saved) => live && restore(saved))
    } else {
      load()
        .then((session) => live && open(session))
        .catch((e) => live && setError(String(e)))
    }

    return () => {
      live = false
    }
    // `query` is created once when training starts and kept in the route's state, so it is
    // referentially stable across renders — no need to pick it apart into primitives.
  }, [query, resume, load, open, outbox, restore])

  const save = () => {
    if (!saving || discarded.current || phase === 'loading') {
      return
    }

    const queued = outbox.queued()

    if (phase !== 'drill' && !(phase === 'done' && queued.length > 0)) {
      clearSavedSession()

      return
    }

    // An answered card is behind the learner already: save the one they land on next.
    const answered = card !== null && answeredCard.current === card

    writeSavedSession({
      userId: saving.userId,
      savedAt: Date.now(),
      query,
      title: saving.title,
      phase,
      offer,
      words,
      scope,
      played: answered && played ? { ...played, index: played.index + 1 } : played,
      drill: answered ? (pending.current ?? drill) : drill,
      pending: queued,
    })
  }

  useEffect(() => {
    saveRef.current = save
  })

  // Every change to the queue is saved too, so the saved round never lists an answer the
  // server already has — a resume would post it a second time.
  useEffect(() => outbox.subscribe(() => saveRef.current()), [outbox])

  // Saved on every move of the round. The dependencies are what a saved round is made of;
  // the queue has its own trigger through the outbox.
  useEffect(() => {
    saveRef.current()
  }, [phase, played, drill, picked])

  /// The learner left the round on purpose: nothing is offered back to them later.
  const discard = useCallback(() => {
    discarded.current = true
    clearSavedSession()
  }, [])

  const answer = useCallback(
    (choice: string) => {
      if (!card || answeredCard.current === card || phase !== 'drill') {
        return
      }

      answeredCard.current = card
      const known = choice === card.exercise.answer

      // Ahead of `outbox.add`, which saves the round: the drill state after this answer is
      // the one a reload has to land on.
      if (drill) {
        pending.current = applyAnswer(drill, known)
      }

      outbox.add({
        verb: card.verb.v1,
        promptForm: card.promptForm,
        chosen: choice,
        responseMs: Math.min(Math.max(0, Date.now() - shownAt.current), MaxResponseMs),
        mode: query.mode,
        group: query.group,
      })

      // The round never waits on this — it only decides whether the screen carries a quiet
      // note about answers that have not left yet.
      outbox
        .flush()
        .then(() => setStuck(false))
        .catch(() => setStuck(true))

      // A right pick needs nothing on screen: the next card comes at once. A wrong one is the
      // moment the form is learned, so it stays until the learner has read it.
      if (known) {
        advance()
      } else {
        setPicked(choice)
      }
    },
    [advance, card, drill, outbox, phase, query.group, query.mode],
  )

  const next = useCallback(() => {
    if (!card) {
      return
    }

    advance()
  }, [advance, card])

  const nextWords = useCallback(() => {
    setSyncing(true)
    outbox
      .flush()
      .then(async () => {
        // Flushed: the server's window is now built on every answer of this session.
        setStuck(false)
        open(await load())
      })
      .catch((e) => {
        // Not flushed — the next window would be built on a standing the server has not seen,
        // and would hand back the same words. Say so and stay put.
        setStuck(true)
        setError(String(e))
      })
      .finally(() => setSyncing(false))
  }, [load, open, outbox])

  const passedHere = drill
    ? drill.words.filter((w) => w.streak >= PassStreak && w.verb.streak < PassStreak).length
    : 0

  return {
    phase,
    error,
    busy,
    syncing,
    stuck,
    card,
    offer,
    words,
    setWords: (words: number) => setWords(words),
    start,
    answer,
    picked,
    revealed: picked !== null,
    next,
    scope: { passed: scope.passed + passedHere, total: scope.total },
    nextWords,
    retryFlush: nextWords,
    discard,
  }
}

/** A free run's queue, joined to the verb data it names; a form's repeats walk its exercises. */
function cardsOf(session: VerbSession): Card[] {
  const byV1 = new Map(session.verbs.map((verb) => [verb.v1, verb]))
  const shown = new Map<string, number>()

  return (session.queue ?? [])
    .map((entry) => {
      const verb = byV1.get(entry.verb)

      if (!verb) {
        return null
      }

      const key = `${verb.v1}:${entry.promptForm}`
      const nth = shown.get(key) ?? 0
      shown.set(key, nth + 1)

      return { verb, promptForm: entry.promptForm, exercise: exerciseFor(verb, entry.promptForm, nth) }
    })
    .filter((card): card is Card => card !== null)
}
