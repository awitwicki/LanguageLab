import { useEffect } from 'react'
import type { DrillQuery } from '../api/client'
import { formatInt } from '../lib/format'
import type { SavedSession } from './savedSession'
import { useVerbSession } from './useVerbSession'
import { VerbSessionStartScreen } from './VerbSessionStartScreen'
import './VerbDrillScreen.css'

interface Props {
  query: DrillQuery
  title: string
  /// Whose device-saved round this is; see `savedSession.ts`.
  userId: number
  /// A round saved before a reload, to carry on from instead of fetching a new one.
  resume?: SavedSession | null
  onBack: () => void
  /// A finished stage has nothing left to drill in order, so the only way on is a free run.
  onStartDrill: (query: DrillQuery, title: string) => void
}

/// One card at a time: a sentence with a blank and the forms to fill it with. A right pick
/// moves straight on; a wrong one shows the right form and the verb's three forms until Next.
export function VerbDrillScreen({ query, title, userId, resume, onBack: leave, onStartDrill }: Props) {
  const session = useVerbSession(query, { userId, title, resume })
  // Leaving on purpose forgets the round; only a reload or a closed tab keeps it.
  const onBack = () => {
    session.discard()
    leave()
  }
  const { phase, card, revealed, picked, error, busy, syncing, stuck } = session

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // A held key auto-repeats every ~30ms; without this a held number or Space would race
      // through cards faster than React can re-render between them.
      if (event.repeat) {
        return
      }

      // A keyboard user tabbed to a real control (a stepper, Back, an option) already has that
      // control's own key handling; this shortcut is only for the page at large, not for
      // hijacking Enter/Space/numbers away from whatever is focused.
      const target = event.target
      if (target instanceof HTMLElement && target.closest('button, [role="button"], input, textarea, select')) {
        return
      }

      const enter = event.key === 'Enter'
      const space = event.key === ' '

      if (phase === 'start') {
        if (enter) {
          event.preventDefault()
          session.start()
        }

        return
      }

      if (phase !== 'drill' || !card) {
        return
      }

      if (revealed) {
        if (space || enter) {
          event.preventDefault()
          session.next()
        }

        return
      }

      const index = Number(event.key) - 1
      const choice = Number.isInteger(index) ? card.exercise.options[index] : undefined

      if (choice !== undefined) {
        event.preventDefault()
        session.answer(choice)
      }
    }

    window.addEventListener('keydown', onKey)

    return () => window.removeEventListener('keydown', onKey)
  }, [card, phase, revealed, session])

  return (
    <>
      <div className="drill-head">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          Back
        </button>
        <span className="footnote">{title}</span>
        {phase !== 'loading' && phase !== 'finished' && (
          <span className="footnote num">
            {formatInt(session.scope.passed)} of {formatInt(session.scope.total)} passed
          </span>
        )}
      </div>

      {error && <p className="error">{error}</p>}

      {/* The round carries on either way — this only says the answers have not left yet. */}
      {stuck && !error && (
        <p className="footnote drill-stuck">Your answers will be saved once the connection is back.</p>
      )}

      {phase === 'start' && (
        <VerbSessionStartScreen
          offer={session.offer}
          words={session.words}
          onWords={session.setWords}
          onStart={session.start}
        />
      )}

      {phase === 'finished' && (
        <div className="card drill-done">
          <p>Every verb of this stage has passed. Free training keeps them fresh.</p>
          <div className="drill-done-actions">
            {query.group !== undefined && (
              <button
                type="button"
                className="btn btn-primary drill-free"
                onClick={() => onStartDrill({ mode: 'free', group: query.group, scope: 'stage' }, title)}
              >
                Train freely
              </button>
            )}
            <button type="button" className="btn btn-secondary" onClick={onBack}>
              Back to the stage
            </button>
          </div>
        </div>
      )}

      {phase === 'done' && (
        <div className="card drill-done">
          <p>This batch has passed. The next words are waiting.</p>
          {syncing && <p className="footnote">Saving your answers…</p>}
          <div className="drill-done-actions">
            <button
              type="button"
              className="btn btn-primary drill-next-words"
              onClick={session.nextWords}
              disabled={syncing}
            >
              Take the next words
            </button>
            <button type="button" className="btn btn-secondary" onClick={onBack}>
              Back to the stage
            </button>
          </div>
        </div>
      )}

      {phase === 'drill' && !card && busy && <p className="footnote">Loading…</p>}

      {phase === 'drill' && card && (
        <div className="card drill-card">
          <p className="drill-sentence">
            {card.exercise.before}
            <span className={`drill-blank${revealed ? ' is-filled' : ''}`}>
              {revealed ? card.exercise.answer : '___'}
            </span>
            {card.exercise.after}
          </p>
          <p className="drill-translation">{card.verb.translation}</p>

          <div className="drill-options">
            {card.exercise.options.map((option, index) => (
              <button
                key={option}
                type="button"
                data-option={option}
                className={`btn btn-lg drill-option ${optionClass(option, card.exercise.answer, picked)}`}
                aria-pressed={option === picked}
                onClick={() => session.answer(option)}
              >
                {option} <kbd>{index + 1}</kbd>
              </button>
            ))}
          </div>

          {revealed && (
            <div className="drill-after">
              <p className="drill-triplet">{`${card.verb.v1} – ${card.verb.v2} – ${card.verb.v3}`}</p>
              {card.verb.note && <p className="footnote">{card.verb.note}</p>}
              {/* A real click leaves the picked option focused, since it stays on screen for
                  its colour instead of unmounting; autoFocus moves focus here so Space/Enter
                  reaches Next natively rather than re-activating the stale option. */}
              <button
                type="button"
                className="btn btn-primary btn-lg drill-next"
                onClick={session.next}
                autoFocus
              >
                Next <kbd>Space</kbd>
              </button>
            </div>
          )}
        </div>
      )}
    </>
  )
}

/// Before a pick every option is plain. After a wrong one the right option turns green and the
/// picked one red, reusing the sorting screen's known/unknown buttons, which the contrast test
/// already covers. The options stay enabled — a disabled button would fade the colours — and
/// the hook ignores a second pick on the same card.
function optionClass(option: string, answer: string, picked: string | null): string {
  if (picked === null) {
    return 'btn-secondary'
  }

  if (option === answer) {
    return 'btn-known'
  }

  return option === picked ? 'btn-unknown' : 'btn-secondary'
}
