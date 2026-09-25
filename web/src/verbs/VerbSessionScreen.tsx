import { useEffect, useRef, useState } from 'react'
import { api, type SessionStarted, type TaskDto } from '../api/client'
import { formatInt } from '../lib/format'
import { triplet } from './format'
import { CardTask } from './tasks/CardTask'
import { FormPickTask } from './tasks/FormPickTask'
import { GapChoiceTask } from './tasks/GapChoiceTask'
import { GapTypeTask } from './tasks/GapTypeTask'
import { MatchTask } from './tasks/MatchTask'
import { OddOneTask } from './tasks/OddOneTask'
import { TripleTypeTask } from './tasks/TripleTypeTask'
import { useVerbSession } from './useVerbSession'
import './VerbSessionScreen.css'

interface Props {
  sessionId: number
  /** The nav link mid-session and the summary's Done button both return here — the session stays open either way. */
  onBack: () => void
  onRepeatErrors: (started: SessionStarted) => void
}

/** Number-key shortcuts for the choice exercises; null when the key does not apply to this task. */
function keyAnswer(task: TaskDto, key: string): string | null {
  if (task.type === 'gapChoice' && key >= '1' && key <= '9') {
    return task.gapChoice!.options[Number(key) - 1] ?? null
  }

  if (task.type === 'oddOne' && key >= '1' && key <= '9') {
    return task.oddOne!.options[Number(key) - 1] ?? null
  }

  if (task.type === 'formPick' && (key === '1' || key === '2')) {
    return key === '1' ? 'v2' : 'v3'
  }

  return null
}

function TaskBody({
  task,
  disabled,
  onAnswer,
  hint,
}: {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
  hint: string | null
}) {
  switch (task.type) {
    case 'card':
      return <CardTask task={task} disabled={disabled} onAnswer={onAnswer} />
    case 'gapChoice':
      return <GapChoiceTask task={task} disabled={disabled} onAnswer={onAnswer} />
    case 'oddOne':
      return <OddOneTask task={task} disabled={disabled} onAnswer={onAnswer} />
    case 'match':
      return <MatchTask task={task} disabled={disabled} onAnswer={onAnswer} />
    case 'formPick':
      return <FormPickTask task={task} disabled={disabled} onAnswer={onAnswer} />
    case 'gapType':
      return <GapTypeTask task={task} disabled={disabled} onAnswer={onAnswer} hint={hint} />
    case 'tripleType':
      return <TripleTypeTask task={task} disabled={disabled} onAnswer={onAnswer} />
  }
}

export function VerbSessionScreen({ sessionId, onBack, onRepeatErrors }: Props) {
  const { status, error, task, answered, total, feedback, neutralHint, summary, answer, next, forgot } =
    useVerbSession(sessionId)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [showTranslation, setShowTranslation] = useState(false)
  const [repeatBusy, setRepeatBusy] = useState(false)
  const nextButtonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    setShowTranslation(false)
    setNotice(null)
  }, [task?.id])

  useEffect(() => {
    if (status === 'feedback') {
      nextButtonRef.current?.focus()
    }
  }, [status])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const onButton = event.target instanceof HTMLButtonElement
      const onInput = event.target instanceof HTMLInputElement
      let handled = true

      if (status === 'feedback' && event.key === 'Enter' && !onButton) {
        next()
      } else if (status === 'task' && task?.type === 'card' && (event.key === 'Enter' || event.key === ' ') && !onButton) {
        void submit('seen')
      } else if (status === 'task' && task && !onInput && !onButton) {
        const value = keyAnswer(task, event.key)
        if (value !== null) void submit(value)
        else handled = false
      } else {
        handled = false
      }

      if (handled) {
        event.preventDefault()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, task, next])

  const submit = async (value: string) => {
    if (busy) {
      return
    }

    setBusy(true)

    try {
      await answer(value)
    } finally {
      setBusy(false)
    }
  }

  const doForgot = async (v1: string) => {
    const message = await forgot(v1)
    setNotice(message)
  }

  const repeatErrors = async () => {
    setRepeatBusy(true)

    try {
      const started = await api.startVerbSession({ mode: 'errorsOnly', fromSessionId: sessionId })
      onRepeatErrors(started)
    } finally {
      setRepeatBusy(false)
    }
  }

  if (status === 'loading') {
    return <p className="footnote">Loading…</p>
  }

  if (status === 'error') {
    return <p className="error">{error}</p>
  }

  if (status === 'summary' && summary) {
    return (
      <section className="verb-summary">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ Irregular verbs
        </button>
        <h1 className="large-title num">
          {formatInt(summary.correct)} of {formatInt(summary.total)}
        </h1>

        {summary.mistakes.length > 0 && (
          <ul className="verb-mistakes">
            {summary.mistakes.map((m) => (
              <li key={m.v1} className="verb-mistake-row">
                <span>{triplet(m.v1, m.v2, m.v3)}</span>
                <span className="verb-mistake-translation">{m.translation}</span>
                <button type="button" className="btn btn-quiet" onClick={() => void doForgot(m.v1)}>
                  Forgot
                </button>
              </li>
            ))}
          </ul>
        )}

        {notice && <p className="footnote verb-notice">{notice}</p>}

        <div className="verb-summary-actions">
          {summary.mistakes.length > 0 && (
            <button type="button" className="btn btn-primary repeat-errors-btn" disabled={repeatBusy} onClick={() => void repeatErrors()}>
              Repeat errors
            </button>
          )}
          <button type="button" className="btn btn-secondary done-btn" onClick={onBack}>
            Done
          </button>
        </div>
      </section>
    )
  }

  if (!task) {
    return null
  }

  if (status === 'feedback' && feedback) {
    return (
      <>
        <div className="verb-session-nav">
          <button type="button" className="btn btn-quiet" onClick={onBack}>
            ‹ Irregular verbs
          </button>
        </div>

        <div className="verb-feedback is-wrong">
          <p className="verb-feedback-triplet">{feedback.triplet}</p>
          <p className="verb-feedback-explanation">{feedback.explanation}</p>
          <div className="verb-feedback-actions">
            <button type="button" className="btn btn-quiet" onClick={() => void doForgot(task.verb.v1)}>
              Forgot — repeat
            </button>
            <button ref={nextButtonRef} type="button" className="btn btn-primary btn-lg next-btn" onClick={next}>
              Next <kbd>Enter</kbd>
            </button>
          </div>
        </div>

        {notice && <p className="footnote verb-notice">{notice}</p>}
      </>
    )
  }

  return (
    <>
      <div className="verb-session-nav">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ Irregular verbs
        </button>
        <button type="button" className="btn btn-quiet forgot-btn" onClick={() => void doForgot(task.verb.v1)}>
          Forgot
        </button>
      </div>

      <p className="footnote verb-progress num">
        Card {formatInt(answered + 1)} of {formatInt(total)}
      </p>

      {task.type !== 'card' && (
        <p className="footnote verb-translation-toggle">
          {showTranslation ? (
            task.verb.translation
          ) : (
            <button type="button" className="btn btn-quiet" onClick={() => setShowTranslation(true)}>
              Show translation
            </button>
          )}
        </p>
      )}

      <TaskBody task={task} disabled={busy} onAnswer={(value) => void submit(value)} hint={neutralHint} />

      {notice && <p className="footnote verb-notice">{notice}</p>}
    </>
  )
}
