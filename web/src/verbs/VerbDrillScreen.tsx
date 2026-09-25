import { useEffect } from 'react'
import type { DrillQuery } from '../api/client'
import { formatInt } from '../lib/format'
import { useDrill } from './useDrill'
import './VerbDrillScreen.css'

interface Props {
  query: DrillQuery
  title: string
  onBack: () => void
}

/// One card at a time: a form to recognise, the three forms blurred underneath, and the
/// learner's own verdict. The verdict unblurs the answer; the level comes out of how fast
/// it came.
export function VerbDrillScreen({ query, title, onBack }: Props) {
  const { card, revealed, done, busy, error, answer, next } = useDrill(query)

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
          <button type="button" className="btn btn-primary" onClick={onBack}>
            Back to the stage
          </button>
        </div>
      )}

      {card && (
        <div className="card drill-card">
          <p className="drill-prompt">{promptOf(card.verb, card.promptForm)}</p>

          <div className={`drill-answer${revealed ? '' : ' is-blurred'}`}>
            <p className="drill-triplet">{`${card.verb.v1} – ${card.verb.v2} – ${card.verb.v3}`}</p>
            <p className="drill-translation">{card.verb.translation}</p>
          </div>

          {revealed ? (
            <div className="drill-after">
              <p className="drill-example">{card.example.text.replace(/\[([^\]]+)\]/, '$1')}</p>
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

function promptOf(verb: { v1: string; v2: string; v3: string }, form: string): string {
  const text = form === 'v1' ? verb.v1 : form === 'v2' ? verb.v2 : verb.v3

  // `be` has "was / were"; one form is enough to recognise it by.
  return text.split(' / ')[0]
}
