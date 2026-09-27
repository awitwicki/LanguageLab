import { useEffect, type KeyboardEvent as ReactKeyboardEvent } from 'react'
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

/// One card at a time: a form to recognise and the three forms blurred underneath. The
/// introduction round only moves on; the drill round takes the learner's verdict, and a miss
/// is what uncovers the answer.
export function VerbDrillScreen({ query, title, userId, resume, onBack: leave, onStartDrill }: Props) {
  const session = useVerbSession(query, { userId, title, resume })
  // Leaving on purpose forgets the round; only a reload or a closed tab keeps it.
  const onBack = () => {
    session.discard()
    leave()
  }
  const { phase, card, intro, revealed, peeked, error, busy, syncing, stuck } = session
  const shown = revealed || peeked

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // A held key auto-repeats every ~30ms; without this a held arrow or Space would race
      // through cards faster than React can re-render between them.
      if (event.repeat) {
        return
      }

      // A keyboard user tabbed to a real control (a stepper, Skip to training, Back, the
      // blurred answer) already has that control's own key handling; this shortcut is only for
      // the page at large, not for hijacking Enter/Space away from whatever is focused.
      const target = event.target
      if (target instanceof HTMLElement && target.closest('button, [role="button"], input, textarea, select')) {
        return
      }

      const enter = event.key === 'Enter'
      const space = event.key === ' '

      if (phase === 'start') {
        if (enter) {
          event.preventDefault()
          session.startIntro()
        }

        return
      }

      if (phase === 'intro' || revealed) {
        if (space || enter) {
          event.preventDefault()
          session.next()
        }

        return
      }

      if (phase === 'drill') {
        if (event.key === 'ArrowRight') session.answer(true)
        if (event.key === 'ArrowLeft') session.answer(false)
      }
    }

    window.addEventListener('keydown', onKey)

    return () => window.removeEventListener('keydown', onKey)
  }, [phase, revealed, session])

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
          settings={session.settings}
          onWords={session.setWords}
          onRounds={session.setRounds}
          onStart={session.startIntro}
          onSkip={session.skipIntro}
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

      {(phase === 'intro' || phase === 'drill') && !card && busy && <p className="footnote">Loading…</p>}

      {(phase === 'intro' || phase === 'drill') && card && (
        <div className="card drill-card">
          {intro && (
            <p className="footnote num drill-intro-step">
              {formatInt(intro.step)} of {formatInt(intro.total)}
            </p>
          )}

          <p className="drill-prompt">{promptOf(card.verb, card.promptForm)}</p>

          {/* Blurred, the answer is a button: tapping it uncovers the forms without judging
              the card, and its own text stays out of the screen-reader tree until then — the
              label is what a reader announces instead. */}
          <div
            className={`drill-answer${shown ? '' : ' is-blurred'}`}
            {...(shown
              ? {}
              : {
                  role: 'button',
                  tabIndex: 0,
                  'aria-label': 'Show the answer',
                  onClick: session.peek,
                  onKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault()
                      session.peek()
                    }
                  },
                })}
          >
            <p className="drill-triplet" aria-hidden={shown ? undefined : true}>
              {`${card.verb.v1} – ${card.verb.v2} – ${card.verb.v3}`}
            </p>
            <p className="drill-translation" aria-hidden={shown ? undefined : true}>
              {card.verb.translation}
            </p>
          </div>

          {phase === 'intro' ? (
            <div className="drill-after">
              <button type="button" className="btn btn-primary btn-lg drill-next" onClick={session.next}>
                Next <kbd>Space</kbd>
              </button>
              <button type="button" className="btn btn-quiet drill-skip-intro" onClick={session.skipIntro}>
                Skip introduction
              </button>
            </div>
          ) : revealed ? (
            <div className="drill-after">
              <Example text={exampleOf(card.verb, card.promptForm)} />
              {card.verb.note && <p className="footnote">{card.verb.note}</p>}
              <button type="button" className="btn btn-primary btn-lg drill-next" onClick={session.next}>
                Next <kbd>Space</kbd>
              </button>
            </div>
          ) : (
            <div className="drill-verdict">
              <button type="button" className="btn btn-lg btn-unknown" onClick={() => session.answer(false)}>
                I don't know <kbd>←</kbd>
              </button>
              <button type="button" className="btn btn-lg btn-known" onClick={() => session.answer(true)}>
                I know <kbd>→</kbd>
              </button>
            </div>
          )}
        </div>
      )}
    </>
  )
}

/// The catalog wraps the verb form in square brackets — "They [went] home early." — so the
/// sentence can show which word is the point. The brackets themselves never reach the page.
function Example({ text }: { readonly text: string }) {
  const parts = /^([^[]*)\[([^\]]+)\](.*)$/s.exec(text)

  if (!parts) {
    return <p className="drill-example">{text}</p>
  }

  const [, before, form, after] = parts

  return (
    <p className="drill-example">
      {before}
      <strong>{form}</strong>
      {after}
    </p>
  )
}

/// The example of the tense the prompted form belongs to.
function exampleOf(
  verb: { examples: { present: string; past: string; perfect: string } },
  form: string,
): string {
  return form === 'v1' ? verb.examples.present : form === 'v2' ? verb.examples.past : verb.examples.perfect
}

function promptOf(verb: { v1: string; v2: string; v3: string }, form: string): string {
  const text = form === 'v1' ? verb.v1 : form === 'v2' ? verb.v2 : verb.v3

  // `be` has "was / were"; one form is enough to recognise it by.
  return text.split(' / ')[0]
}
