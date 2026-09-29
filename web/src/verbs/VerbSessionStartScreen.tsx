import type { SessionVerb } from '../api/client'
import { formatInt } from '../lib/format'
import './VerbSessionStartScreen.css'

interface Props {
  /** The window's words, least known first; a session takes the first `words` of them. */
  offer: SessionVerb[]
  words: number
  onWords: (words: number) => void
  onStart: () => void
}

/// Before ordinary training: which words are coming and how many of them to take.
export function VerbSessionStartScreen({ offer, words, onWords, onStart }: Props) {
  return (
    <div className="card start-card">
      <h1 className="title">Ready to train</h1>

      <p className="footnote start-words">{offer.slice(0, words).map((v) => v.v1).join(', ')}</p>

      <div className="start-steppers">
        <Stepper label="Words" name="words" value={words} low={1} high={offer.length} onChange={onWords} />
      </div>

      <div className="start-actions">
        <button type="button" className="btn btn-primary btn-lg start-train" onClick={onStart}>
          Start training <kbd>Enter</kbd>
        </button>
      </div>
    </div>
  )
}

function Stepper({
  label,
  name,
  value,
  low,
  high,
  onChange,
}: {
  readonly label: string
  readonly name: string
  readonly value: number
  readonly low: number
  readonly high: number
  readonly onChange: (value: number) => void
}) {
  return (
    <div className="start-stepper">
      <span className="footnote">{label}</span>
      <div className="start-stepper-row">
        <button
          type="button"
          className={`btn btn-quiet start-${name}-less`}
          aria-label={`One ${label.toLowerCase().replace(/s$/, '')} fewer`}
          disabled={value <= low}
          onClick={() => onChange(value - 1)}
        >
          −
        </button>
        <span className="num start-stepper-value">{formatInt(value)}</span>
        <button
          type="button"
          className={`btn btn-quiet start-${name}-more`}
          aria-label={`One ${label.toLowerCase().replace(/s$/, '')} more`}
          disabled={value >= high}
          onClick={() => onChange(value + 1)}
        >
          +
        </button>
      </div>
    </div>
  )
}
