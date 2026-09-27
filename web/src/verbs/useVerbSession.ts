import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api, type DrillQuery, type SessionVerb, type VerbSession } from '../api/client'
import { readIntroSettings, writeIntroSettings, type IntroSettings } from './introSettings'
import { createOutbox } from './outbox'
import { applyAnswer, createDrill, introQueue, isDone, PassStreak, type Card, type DrillState } from './session'

/// What the server keeps at most (VerbScoring.MaxResponseMs). Clamping here as well keeps a
/// card left open for weeks from overflowing the int the request is typed with.
const MaxResponseMs = 60_000

/// How few cards may be left of a free run's queue before the next chunk is fetched.
const RefillAt = 5

export type Phase = 'loading' | 'start' | 'intro' | 'drill' | 'done' | 'finished'

/** The introduction round, and a free run's queue: a fixed list played in order. */
interface Played {
  kind: 'intro' | 'queue'
  cards: Card[]
  index: number
}

/**
 * One training session, run in the browser. The server hands over the words and their
 * standing; both rounds and the choice of the next card happen here, so no click waits on the
 * network. Answers leave through the outbox in the background.
 *
 * Ordinary training opens on the start screen (`start`), walks the introduction (`intro`) and
 * then the drill round (`drill`), and ends on `done` — or on `finished` when the whole stage
 * has already passed. A free run has nothing to introduce and opens straight on `drill`.
 */
export function useVerbSession(query: DrillQuery) {
  const [phase, setPhase] = useState<Phase>('loading')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [syncing, setSyncing] = useState(false)
  const [stuck, setStuck] = useState(false)
  const [offer, setOffer] = useState<SessionVerb[]>([])
  const [settings, setSettings] = useState<IntroSettings>({ words: 0, rounds: 0 })
  const [scope, setScope] = useState({ passed: 0, total: 0 })
  const [played, setPlayed] = useState<Played | null>(null)
  const [drill, setDrill] = useState<DrillState | null>(null)
  const [revealed, setRevealed] = useState(false)
  const [peeked, setPeeked] = useState(false)

  const outbox = useMemo(() => createOutbox(api.postVerbAnswers), [])
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
    setRevealed(false)
    setPeeked(false)
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
      setSettings(readIntroSettings(session.verbs.length))
      setPlayed(null)
      setDrill(null)
      setPhase('start')
    },
    [startedAt],
  )

  useEffect(() => {
    let live = true

    load()
      .then((session) => live && open(session))
      .catch((e) => live && setError(String(e)))

    return () => {
      live = false
    }
    // `query` is created once when training starts and kept in the route's state, so it is
    // referentially stable across renders — no need to pick it apart into primitives.
  }, [query, load, open])

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
    (words: SessionVerb[]) => {
      setPlayed(null)
      setDrill(createDrill(words))
      setPhase(words.length === 0 ? 'done' : 'drill')
      startedAt()
    },
    [startedAt],
  )

  const chosen = useCallback(() => offer.slice(0, settings.words), [offer, settings.words])

  const startIntro = useCallback(() => {
    writeIntroSettings(settings)
    const cards = introQueue(chosen(), settings.rounds)

    if (cards.length === 0) {
      beginDrill(chosen())

      return
    }

    setPlayed({ kind: 'intro', cards, index: 0 })
    setPhase('intro')
    startedAt()
  }, [beginDrill, chosen, settings, startedAt])

  const skipIntro = useCallback(() => {
    writeIntroSettings(settings)
    beginDrill(chosen())
  }, [beginDrill, chosen, settings])

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

      if (played.kind === 'intro' && index >= played.cards.length) {
        beginDrill(chosen())

        return
      }

      setPlayed({ ...played, index })
      startedAt()

      if (played.kind === 'queue' && played.cards.length - index <= RefillAt) {
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
  }, [beginDrill, chosen, drill, finish, played, refill, startedAt])

  const answer = useCallback(
    (known: boolean) => {
      if (!card || answeredCard.current === card || phase === 'intro') {
        return
      }

      answeredCard.current = card
      outbox.add({
        verb: card.verb.v1,
        promptForm: card.promptForm,
        known,
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

      if (drill) {
        pending.current = applyAnswer(drill, known)
      }

      // "I know" needs no answer on screen: the next card comes at once. A miss is the moment
      // the word is learned, so it stays until the learner has read it.
      if (known) {
        advance()
      } else {
        setRevealed(true)
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

  /// Uncovers the answer without judging the card. The clock keeps running, so a peeked
  /// "I know" scores as the slow answer it is rather than a free full mark.
  const peek = useCallback(() => setPeeked(true), [])

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
    intro:
      played?.kind === 'intro' ? { step: played.index + 1, total: played.cards.length } : null,
    offer,
    settings,
    setWords: (words: number) => setSettings((s) => ({ ...s, words })),
    setRounds: (rounds: number) => setSettings((s) => ({ ...s, rounds })),
    startIntro,
    skipIntro,
    answer,
    revealed,
    peeked,
    peek,
    next,
    scope: { passed: scope.passed + passedHere, total: scope.total },
    nextWords,
    retryFlush: nextWords,
  }
}

/** A free run's queue, joined to the verb data it names. */
function cardsOf(session: VerbSession): Card[] {
  const byV1 = new Map(session.verbs.map((verb) => [verb.v1, verb]))

  return (session.queue ?? [])
    .map((entry) => {
      const verb = byV1.get(entry.verb)

      return verb ? { verb, promptForm: entry.promptForm } : null
    })
    .filter((card): card is Card => card !== null)
}
