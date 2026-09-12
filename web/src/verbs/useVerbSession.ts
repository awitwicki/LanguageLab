import { useCallback, useEffect, useState } from 'react'
import { api, type IrregularVerbSession, type SessionCardView, type VerbForm } from '../api/client'

export const FORMS: VerbForm[] = ['v1', 'v2', 'v3']

type Status = 'loading' | 'active' | 'summary' | 'error'

/** One verb on the table: which forms are closed, which are showing, and the grades given so far. */
export interface SessionCard {
  card: SessionCardView
  /** The two forms that are not open, in v1..v3 order. */
  closed: VerbForm[]
  /** Closed forms showing their word, in reveal order — pre-filled on a retry with the forms already got right. */
  revealed: VerbForm[]
  grades: Partial<Record<VerbForm, boolean>>
  /** Back at the end of the queue after a miss. */
  retry: boolean
}

export interface Retried {
  card: SessionCardView
  missed: VerbForm[]
}

export interface Summary {
  /** Two closed forms per verb of the initial queue. */
  formsTotal: number
  formsRightFirstTime: number
  retried: Retried[]
}

interface State {
  status: Status
  error: string | null
  session: IrregularVerbSession | null
  queue: SessionCard[]
  total: number
  completed: number
  /** "verb:form" keys graded at least once — a retry grade is not a first try. */
  gradedOnce: string[]
  firstTryRight: number
  retried: Retried[]
}

const initialState: State = {
  status: 'loading',
  error: null,
  session: null,
  queue: [],
  total: 0,
  completed: 0,
  gradedOnce: [],
  firstTryRight: 0,
  retried: [],
}

export interface UseVerbSessionResult {
  status: Status
  error: string | null
  session: IrregularVerbSession | null
  current: SessionCard | null
  /** 1-based position of the verb showing now among the session's verbs. */
  verbNumber: number
  total: number
  /** Verbs waiting for a second pass, the current one included. */
  toRetryCount: number
  /** The most recently revealed form without a grade — what the 1 / 2 keys act on. */
  activeForm: VerbForm | null
  /** Every closed form of the current verb is graded, so Next is allowed. */
  isDone: boolean
  /** Reveals that closed form, or the next closed one left to right; a no-op for the open or an already revealed form. */
  reveal: (form?: VerbForm) => void
  /** Grades a revealed, ungraded form and posts it at once. */
  grade: (form: VerbForm, correct: boolean) => void
  next: () => void
  summary: Summary | null
}

function fresh(card: SessionCardView): SessionCard {
  return { card, closed: FORMS.filter((f) => f !== card.open), revealed: [], grades: {}, retry: false }
}

function isDone(entry: SessionCard): boolean {
  return entry.closed.every((f) => entry.grades[f] !== undefined)
}

function activeForm(entry: SessionCard): VerbForm | null {
  const pending = entry.revealed.filter((f) => entry.grades[f] === undefined)
  return pending[pending.length - 1] ?? null
}

function addMiss(retried: Retried[], card: SessionCardView, form: VerbForm): Retried[] {
  const existing = retried.find((r) => r.card.v1 === card.v1)

  if (!existing) {
    return [...retried, { card, missed: [form] }]
  }

  if (existing.missed.includes(form)) {
    return retried
  }

  return retried.map((r) => (r === existing ? { ...r, missed: [...r.missed, form] } : r))
}

/**
 * Drives one session: loads the planned verbs, reveals closed cards, grades each revealed
 * form (posting immediately), and after Next requeues a verb that had a miss — with the
 * forms it got right already showing, so only the missed ones are asked again. The queue
 * is in memory only: a reload loses the position, not the grades already posted. The
 * screen is remounted per step and per run (its key in App), so state starts fresh there.
 */
export function useVerbSession(step: number): UseVerbSessionResult {
  const [state, setState] = useState<State>(initialState)

  useEffect(() => {
    let cancelled = false

    api
      .getVerbSession(step)
      .then((session) => {
        if (cancelled) {
          return
        }

        setState({
          ...initialState,
          status: session.cards.length === 0 ? 'summary' : 'active',
          session,
          queue: session.cards.map(fresh),
          total: session.cards.length,
        })
      })
      .catch((e) => {
        if (!cancelled) {
          setState((s) => ({ ...s, status: 'error', error: String(e) }))
        }
      })

    return () => {
      cancelled = true
    }
  }, [step])

  const reveal = useCallback((form?: VerbForm) => {
    setState((s) => {
      const [current, ...rest] = s.queue

      if (s.status !== 'active' || !current) {
        return s
      }

      const target = form ?? current.closed.find((f) => !current.revealed.includes(f))

      if (!target || !current.closed.includes(target) || current.revealed.includes(target)) {
        return s
      }

      return { ...s, queue: [{ ...current, revealed: [...current.revealed, target] }, ...rest] }
    })
  }, [])

  // Reads `state` from the closure and posts exactly once, outside any setState updater:
  // Strict Mode double-invokes updaters, so a side effect inside one would post twice —
  // the same shape as useSortingQueue's `mark`.
  const grade = useCallback(
    (form: VerbForm, correct: boolean) => {
      const [current, ...rest] = state.queue

      if (state.status !== 'active' || !current) {
        return
      }

      if (!current.revealed.includes(form) || current.grades[form] !== undefined) {
        return
      }

      void api.gradeVerbForm(current.card.v1, form, correct)

      const key = `${current.card.v1}:${form}`
      const firstTry = !state.gradedOnce.includes(key)

      setState({
        ...state,
        queue: [{ ...current, grades: { ...current.grades, [form]: correct } }, ...rest],
        gradedOnce: firstTry ? [...state.gradedOnce, key] : state.gradedOnce,
        firstTryRight: firstTry && correct ? state.firstTryRight + 1 : state.firstTryRight,
        retried: correct ? state.retried : addMiss(state.retried, current.card, form),
      })
    },
    [state],
  )

  const next = useCallback(() => {
    setState((s) => {
      const [current, ...rest] = s.queue

      if (s.status !== 'active' || !current || !isDone(current)) {
        return s
      }

      const kept = current.closed.filter((f) => current.grades[f] === true)

      if (kept.length < current.closed.length) {
        // Second pass: the forms got right stay showing and graded; the missed ones close again.
        const grades = Object.fromEntries(kept.map((f) => [f, true])) as Partial<Record<VerbForm, boolean>>
        return { ...s, queue: [...rest, { ...current, revealed: kept, grades, retry: true }] }
      }

      const completed = s.completed + 1

      return rest.length === 0
        ? { ...s, queue: rest, completed, status: 'summary' }
        : { ...s, queue: rest, completed }
    })
  }, [])

  const current = state.queue[0] ?? null

  return {
    status: state.status,
    error: state.error,
    session: state.session,
    current,
    verbNumber: Math.min(state.completed + 1, state.total),
    total: state.total,
    toRetryCount: state.queue.filter((e) => e.retry).length,
    activeForm: current ? activeForm(current) : null,
    isDone: current ? isDone(current) : false,
    reveal,
    grade,
    next,
    summary:
      state.status === 'summary'
        ? { formsTotal: state.total * 2, formsRightFirstTime: state.firstTryRight, retried: state.retried }
        : null,
  }
}
