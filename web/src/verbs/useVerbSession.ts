import { useCallback, useEffect, useState } from 'react'
import { api, type AnswerFeedback, type SessionSummary, type TaskDto } from '../api/client'

type Status = 'loading' | 'task' | 'feedback' | 'summary' | 'error'

interface State {
  status: Status
  error: string | null
  task: TaskDto | null
  answered: number
  total: number
  feedback: AnswerFeedback | null
  neutralHint: string | null
  summary: SessionSummary | null
}

const initialState: State = {
  status: 'loading',
  error: null,
  task: null,
  answered: 0,
  total: 0,
  feedback: null,
  neutralHint: null,
  summary: null,
}

export interface UseVerbSessionResult {
  status: Status
  error: string | null
  task: TaskDto | null
  answered: number
  total: number
  feedback: AnswerFeedback | null
  /** Set after a near-miss spelling: the task stays open, this is the inline hint. */
  neutralHint: string | null
  summary: SessionSummary | null
  /** Posts one answer. Correct/wrong shows feedback; neutral or a partial Match keeps the task open. */
  answer: (text: string, responseMs?: number) => Promise<void>
  /** Loads the next task, or finishes the session and shows the summary once the queue is empty. */
  next: () => void
  /** Flags a verb as forgotten (defaults to the task currently showing) and returns a short notice. */
  forgot: (v1?: string) => Promise<string>
}

/**
 * Drives one session screen: loads the current task from the server (so a reload just
 * asks for "next" again), posts each answer, and either shows feedback or keeps the
 * task open (neutral spelling, or a Match with pairs still unresolved). When the queue
 * is empty it finishes the session and switches to the summary.
 */
export function useVerbSession(sessionId: number): UseVerbSessionResult {
  const [state, setState] = useState<State>(initialState)

  const load = useCallback(() => {
    setState((s) => ({ ...s, status: 'loading', feedback: null, neutralHint: null }))

    api
      .nextVerbTask(sessionId)
      .then(async (view) => {
        if (view.task === null) {
          const summary = await api.finishVerbSession(sessionId)
          setState((s) => ({ ...s, status: 'summary', summary, task: null, answered: view.answered, total: view.total }))
          return
        }

        setState((s) => ({
          ...s,
          status: 'task',
          task: view.task,
          answered: view.answered,
          total: view.total,
          feedback: null,
          neutralHint: null,
        }))
      })
      .catch((e) => setState((s) => ({ ...s, status: 'error', error: String(e) })))
  }, [sessionId])

  useEffect(() => {
    setState(initialState)
    load()
  }, [load])

  const answer = useCallback(
    async (text: string, responseMs?: number) => {
      const task = state.task

      if (state.status !== 'task' || !task) {
        return
      }

      const result = await api.answerVerbTask(sessionId, task.id, text, responseMs)

      // null — the task was already answered (a race); the queue is asked for again.
      if (!result) {
        load()
        return
      }

      if (result.outcome === 'neutral') {
        setState((s) => ({ ...s, neutralHint: result.explanation }))
        return
      }

      if (!result.taskComplete && result.matched) {
        setState((s) => ({
          ...s,
          task: s.task && s.task.match ? { ...s.task, match: { ...s.task.match, matched: result.matched! } } : s.task,
        }))
        return
      }

      setState((s) => ({ ...s, status: 'feedback', feedback: result }))
    },
    [state.status, state.task, sessionId, load],
  )

  const forgot = useCallback(
    async (v1?: string) => {
      const verb = v1 ?? state.task?.verb.v1

      if (!verb) {
        return ''
      }

      const result = await api.forgotVerb(verb)
      return `${result.v1} — back to practice`
    },
    [state.task],
  )

  return {
    status: state.status,
    error: state.error,
    task: state.task,
    answered: state.answered,
    total: state.total,
    feedback: state.feedback,
    neutralHint: state.neutralHint,
    summary: state.summary,
    answer,
    next: load,
    forgot,
  }
}
