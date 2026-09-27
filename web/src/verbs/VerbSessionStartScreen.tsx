import type { SessionVerb } from '../api/client'
import { formatInt } from '../lib/format'
import { MaxRounds, type IntroSettings } from './introSettings'
import './VerbSessionStartScreen.css'

interface Props {
  /** The window's words, least known first; a session takes the first `settings.words` of them. */
  offer: SessionVerb[]
  settings: IntroSettings
  onWords: (words: number) => void
  onRounds: (rounds: number) => void
  onStart: () => void
  onSkip: () => void
}

/// Before ordinary training: which words are coming, how many of them to take and how many
/// rounds to meet them in. It opens every time, not only on new words — an introduction done
/// and the batch not yet learned is exactly when it is wanted again.
export function VerbSessionStartScreen({ offer, settings, onWords, onRounds, onStart, onSkip }: Props) {
  const cards = settings.words * settings.rounds

  return (
    <div className="card start-card">
      <h1 className="title">Ready to train</h1>

      <p className="footnote start-words">{offer.slice(0, settings.words).map((v) => v.v1).join(', ')}</p>

      <div className="start-steppers">
        <Stepper
          label="Words"
          name="words"
          value={settings.words}
          low={1}
          high={offer.length}
          onChange={onWords}
        />
        <Stepper
          label="Rounds"
          name="rounds"
          value={settings.rounds}
          low={1}
          high={MaxRounds}
          onChange={onRounds}
        />
      </div>

      <p className="footnote start-summary">
        {plural(settings.words, 'word')} × {plural(settings.rounds, 'round')} = {plural(cards, 'card')}
      </p>

      <div className="start-actions">
        <button type="button" className="btn btn-primary btn-lg start-intro" onClick={onStart}>
          Start introduction <kbd>Enter</kbd>
        </button>
        <button type="button" className="btn btn-secondary start-skip" onClick={onSkip}>
          Skip to training
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

/// "1 word", "5 words" — the summary line reads as a sentence, so the unit agrees with its
/// number. `wordsLabel` is about a dictionary's words and says "words" for a count of one.
function plural(count: number, unit: string): string {
  return `${formatInt(count)} ${unit}${count === 1 ? '' : 's'}`
}
