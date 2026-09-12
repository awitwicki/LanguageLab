import { useEffect, useRef } from 'react'
import type { SessionCardView, VerbForm } from '../api/client'
import { formatInt } from '../lib/format'
import { FORMS, useVerbSession, type SessionCard } from './useVerbSession'
import './VerbSessionScreen.css'

interface Props {
  step: number
  onBack: () => void
  /** Starts a fresh session for the same step — the server re-plans from the grades just posted. */
  onContinue: () => void
}

export const FORM_LABELS: Record<VerbForm, string> = {
  v1: 'V1 · infinitive',
  v2: 'V2 · past simple',
  v3: 'V3 · past participle',
}

type CardState = 'open' | 'closed' | 'revealed' | 'correct' | 'missed'

function cardState(entry: SessionCard, form: VerbForm): CardState {
  if (form === entry.card.open) {
    return 'open'
  }

  if (!entry.revealed.includes(form)) {
    return 'closed'
  }

  const grade = entry.grades[form]
  return grade === undefined ? 'revealed' : grade ? 'correct' : 'missed'
}

function Triplet({ card, missed }: { card: SessionCardView; missed: VerbForm[] }) {
  return (
    <span className="verb-retried-forms">
      {FORMS.map((form, i) => (
        <span key={form}>
          {i > 0 && ' – '}
          <span className={missed.includes(form) ? 'is-missed' : undefined}>{card[form]}</span>
        </span>
      ))}
    </span>
  )
}

export function VerbSessionScreen({ step, onBack, onContinue }: Props) {
  const { status, error, session, current, verbNumber, total, toRetryCount, activeForm, isDone, reveal, grade, next, summary } =
    useVerbSession(step)
  const nextButtonRef = useRef<HTMLButtonElement>(null)

  const canReveal = current ? current.closed.some((f) => !current.revealed.includes(f)) : false

  // The grade buttons disappear with the second grade, so focus would drop to <body>;
  // Next is where the next keyboard action goes.
  useEffect(() => {
    if (isDone) {
      nextButtonRef.current?.focus()
    }
  }, [isDone])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // Space on a focused button must keep activating that button; only a bare Space reveals.
      const onButton = event.target instanceof HTMLButtonElement
      let handled = true

      if (status === 'active' && event.key === ' ' && !onButton && canReveal) {
        reveal()
      } else if (status === 'active' && activeForm && event.key === '1') {
        grade(activeForm, true)
      } else if (status === 'active' && activeForm && event.key === '2') {
        grade(activeForm, false)
      } else if (status === 'active' && isDone && event.key === 'Enter') {
        next()
      } else if (status === 'summary' && event.key === 'Enter') {
        onContinue()
      } else {
        handled = false
      }

      // preventDefault only when a branch acted: on Enter it also stops the focused button's
      // own click, so Next runs once, not twice.
      if (handled) {
        event.preventDefault()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [status, canReveal, isDone, activeForm, reveal, grade, next, onContinue])

  if (status === 'loading') {
    return <p className="footnote">Loading…</p>
  }

  if (status === 'error') {
    return <p className="error">{error}</p>
  }

  const heading = session ? `Step ${session.step} · ${session.title}${session.review ? ' · Review' : ''}` : ''

  if (status === 'summary' && summary) {
    return (
      <section className="verb-summary">
        <p className="footnote">{heading}</p>
        <h1 className="large-title num">
          {formatInt(summary.formsRightFirstTime)} of {formatInt(summary.formsTotal)} forms right first time
        </h1>

        {summary.retried.length > 0 && (
          <ul className="verb-retried">
            {summary.retried.map((r) => (
              <li key={r.card.v1} className="verb-retried-row">
                <Triplet card={r.card} missed={r.missed} />
                <span className="verb-retried-translation">{r.card.translation}</span>
              </li>
            ))}
          </ul>
        )}

        <div className="verb-summary-actions">
          <button type="button" className="btn btn-primary btn-lg" onClick={onContinue}>
            Continue <kbd>Enter</kbd>
          </button>
          <button type="button" className="btn btn-quiet" onClick={onBack}>
            Back to verbs
          </button>
        </div>
      </section>
    )
  }

  if (!current) {
    return null
  }

  return (
    <>
      <div className="verb-nav">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ Irregular verbs
        </button>
      </div>

      <p className="footnote verb-progress num">
        {heading} · Verb {formatInt(verbNumber)} of {formatInt(total)}
        {toRetryCount > 0 && ` · ${formatInt(toRetryCount)} to retry`}
      </p>

      <div className="verb-table">
        <div className="verb-cards">
          {FORMS.map((form) => {
            const state = cardState(current, form)

            return (
              <div key={form} className="verb-card" data-state={state}>
                {state === 'closed' ? (
                  <button
                    type="button"
                    className="verb-card-face verb-card-closed"
                    aria-label={`Reveal ${FORM_LABELS[form]}`}
                    onClick={() => reveal(form)}
                  >
                    ?
                  </button>
                ) : (
                  <p className="verb-card-face">{current.card[form]}</p>
                )}
                <p className="footnote verb-card-label">{FORM_LABELS[form]}</p>
                {state === 'revealed' && (
                  <div className="verb-grade">
                    <button type="button" className="btn btn-secondary verb-got" onClick={() => grade(form, true)}>
                      ✓ Got it {activeForm === form && <kbd>1</kbd>}
                    </button>
                    <button type="button" className="btn btn-secondary verb-missed" onClick={() => grade(form, false)}>
                      ✗ Missed {activeForm === form && <kbd>2</kbd>}
                    </button>
                  </div>
                )}
              </div>
            )
          })}
        </div>

        <p className="verb-translation">{current.card.translation}</p>

        <div className="verb-actions">
          {isDone ? (
            <button ref={nextButtonRef} type="button" className="btn btn-primary btn-lg verb-next" onClick={next}>
              Next <kbd>Enter</kbd>
            </button>
          ) : (
            <button type="button" className="btn btn-quiet" disabled={!canReveal} onClick={() => reveal()}>
              Reveal next <kbd>Space</kbd>
            </button>
          )}
        </div>
      </div>
    </>
  )
}
