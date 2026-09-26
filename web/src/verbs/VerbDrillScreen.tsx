import { useEffect, type KeyboardEvent as ReactKeyboardEvent } from 'react'
import type { DrillQuery } from '../api/client'
import { formatInt } from '../lib/format'
import { useDrill } from './useDrill'
import './VerbDrillScreen.css'

interface Props {
  query: DrillQuery
  title: string
  onBack: () => void
  /// A finished stage has nothing left to drill in order, so the only way on is a free run.
  onStartDrill: (query: DrillQuery, title: string) => void
}

/// One card at a time: a form to recognise, the three forms blurred underneath, and the
/// learner's own verdict. The verdict unblurs the answer; the level comes out of how fast
/// it came.
export function VerbDrillScreen({ query, title, onBack, onStartDrill }: Props) {
  const { card, revealed, peeked, done, busy, error, answer, peek, next } = useDrill(query)
  const shown = revealed !== null || peeked

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (revealed) {
        if (event.key === ' ' || event.key === 'Enter') {
          event.preventDefault()
          next()
        }

        return
      }

      if (event.key === 'ArrowRight') answer(true)
      if (event.key === 'ArrowLeft') answer(false)
    }

    window.addEventListener('keydown', onKey)

    return () => window.removeEventListener('keydown', onKey)
  }, [answer, next, revealed])

  return (
    <>
      <div className="drill-head">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          Back
        </button>
        <span className="footnote">{title}</span>
        {card && (
          <span className="footnote num">
            {formatInt(card.scope.passed)} of {formatInt(card.scope.total)} passed
          </span>
        )}
      </div>

      {error && <p className="error">{error}</p>}

      {done && (
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

      {card && (
        <div className="card drill-card">
          <p className="drill-prompt">{promptOf(card.verb, card.promptForm)}</p>

          {/* Blurred, the answer is a button: tapping it uncovers the forms without
              judging the card, and its own text stays out of the screen-reader tree until
              then — the label is what a reader announces instead. */}
          <div
            className={`drill-answer${shown ? '' : ' is-blurred'}`}
            {...(shown
              ? {}
              : {
                  role: 'button',
                  tabIndex: 0,
                  'aria-label': 'Show the answer',
                  onClick: peek,
                  onKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault()
                      peek()
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

          {revealed ? (
            <div className="drill-after">
              <Example text={card.example.text} />
              {card.verb.note && <p className="footnote">{card.verb.note}</p>}
              <button type="button" className="btn btn-primary btn-lg drill-next" onClick={next} disabled={busy}>
                Next <kbd>Space</kbd>
              </button>
            </div>
          ) : (
            <div className="drill-verdict">
              <button
                type="button"
                className="btn btn-lg btn-unknown"
                onClick={() => answer(false)}
                disabled={busy}
              >
                I don't know <kbd>←</kbd>
              </button>
              <button type="button" className="btn btn-lg btn-known" onClick={() => answer(true)} disabled={busy}>
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

function promptOf(verb: { v1: string; v2: string; v3: string }, form: string): string {
  const text = form === 'v1' ? verb.v1 : form === 'v2' ? verb.v2 : verb.v3

  // `be` has "was / were"; one form is enough to recognise it by.
  return text.split(' / ')[0]
}
