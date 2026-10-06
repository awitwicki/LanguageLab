import { useCallback, useEffect, useState } from 'react'
import { api, type GrammarTopic } from '../api/client'
import { formatInt } from '../lib/format'
import { currentCard, isFinished, next, pick, rightCount, startDrill, type Drill } from './drill'
import './GrammarTopicScreen.css'

/** How long a right pick stays on screen before the next card. */
export const RIGHT_PAUSE_MS = 600

interface Props {
  topicKey: string
  onBack: () => void
}

type Phase = 'learn' | 'drill' | 'result'

/** Text with the catalog's [brackets] shown in bold. */
function Emphasised({ text }: { text: string }) {
  return <>{text.split(/\[([^\]]*)\]/).map((part, i) => (i % 2 === 1 ? <strong key={i}>{part}</strong> : part))}</>
}

/// A topic: its explanation beside its exercises (one at a time, then the score), so the rule
/// stays in view while the learner answers. A right
/// pick moves on by itself; a wrong one shows the right form and why until Next. Nothing is
/// sent or saved — a reload starts the topic over.
export function GrammarTopicScreen({ topicKey, onBack }: Props) {
  const [topic, setTopic] = useState<GrammarTopic | null>(null)
  const [missing, setMissing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [phase, setPhase] = useState<Phase>('learn')
  const [drill, setDrill] = useState<Drill | null>(null)

  useEffect(() => {
    api
      .getGrammarTopics()
      .then((topics) => {
        // A planned topic has no content yet; a route to one is as stale as a route to nothing.
        const found = topics.find((t) => t.key === topicKey && !t.planned)

        if (found) {
          setTopic(found)
        } else {
          setMissing(true)
        }
      })
      .catch((e) => setError(String(e)))
  }, [topicKey])

  // A stale route — the topic left the catalog — goes back to the list.
  useEffect(() => {
    if (missing) {
      onBack()
    }
  }, [missing, onBack])

  const card = drill ? currentCard(drill) : null
  const rightPicked = card !== null && drill?.picked === card.answer
  const wrongPicked = card !== null && drill !== null && drill.picked !== null && !rightPicked

  const begin = useCallback(() => {
    if (topic) {
      setDrill(startDrill(topic.exercises))
      setPhase('drill')
    }
  }, [topic])

  const answer = useCallback((option: string) => setDrill((d) => (d ? pick(d, option) : d)), [])

  const advance = useCallback(() => {
    if (!drill) {
      return
    }

    const after = next(drill)
    setDrill(after)

    if (isFinished(after)) {
      setPhase('result')
    }
  }, [drill])

  useEffect(() => {
    if (phase !== 'drill' || !rightPicked) {
      return
    }

    const timer = setTimeout(advance, RIGHT_PAUSE_MS)

    return () => clearTimeout(timer)
  }, [phase, rightPicked, advance])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // A held key auto-repeats; it must not race through cards.
      if (event.repeat) {
        return
      }

      // A focused control has its own key handling; these shortcuts are for the page at large.
      const target = event.target
      if (target instanceof HTMLElement && target.closest('button, [role="button"], input, textarea, select')) {
        return
      }

      const enter = event.key === 'Enter'
      const space = event.key === ' '

      if (phase === 'learn') {
        if (enter) {
          event.preventDefault()
          begin()
        }

        return
      }

      if (phase !== 'drill' || !card || !drill) {
        return
      }

      if (drill.picked !== null) {
        // During a right pick's pause nothing is waiting for a key.
        if (wrongPicked && (space || enter)) {
          event.preventDefault()
          advance()
        }

        return
      }

      const choice = card.options[Number(event.key) - 1]

      if (choice !== undefined) {
        event.preventDefault()
        answer(choice)
      }
    }

    window.addEventListener('keydown', onKey)

    return () => window.removeEventListener('keydown', onKey)
  }, [phase, card, drill, wrongPicked, begin, advance, answer])

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!topic) {
    return <p className="footnote">Loading…</p>
  }

  return (
    <>
      <div className="grammar-head">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          Back
        </button>
        <span className="footnote">{topic.level}</span>
        {phase === 'drill' && drill && (
          <span className="footnote num grammar-counter">
            {formatInt(drill.index + 1)} / {formatInt(drill.cards.length)}
          </span>
        )}
      </div>

      <div className="grammar-layout">
        <aside className="grammar-lesson">
          <h1 className="large-title">{topic.title}</h1>
          <div className="grammar-explanation">
            {topic.explanation.map((paragraph, i) => (
              <p key={i}>
                <Emphasised text={paragraph} />
              </p>
            ))}
          </div>
          <ul className="grammar-examples">
            {topic.examples.map((example, i) => (
              <li key={i}>
                <Emphasised text={example} />
              </li>
            ))}
          </ul>
        </aside>

        <div className="grammar-work">
          {phase === 'learn' && (
            <button type="button" className="btn btn-primary btn-lg grammar-start" onClick={begin}>
              Start exercises <kbd>Enter</kbd>
            </button>
          )}

          {phase === 'drill' && drill && card && (
            <div className="grammar-card">
              <p className="grammar-sentence">
                {card.before}
                <span className={`grammar-blank${drill.picked !== null ? ' is-filled' : ''}`}>
                  {drill.picked !== null ? card.answer : '___'}
                </span>
                {card.after}
              </p>

              <div className="grammar-options">
                {card.options.map((option, index) => (
                  // Keyed per card: the next card's buttons are new ones, so a clicked option does
                  // not keep focus and swallow the number keys (topics reuse the same options).
                  <button
                    key={`${drill.index}:${option}`}
                    type="button"
                    data-option={option}
                    className={`btn btn-lg grammar-option ${optionClass(option, card.answer, drill.picked)}`}
                    aria-pressed={option === drill.picked}
                    onClick={() => answer(option)}
                  >
                    {option} <kbd>{index + 1}</kbd>
                  </button>
                ))}
              </div>

              {wrongPicked && (
                <div className="grammar-after">
                  <p className="grammar-why">{card.why}</p>
                  {/* autoFocus takes focus off the clicked option, so Space/Enter reach Next. */}
                  <button type="button" className="btn btn-primary btn-lg grammar-next" onClick={advance} autoFocus>
                    Next <kbd>Space</kbd>
                  </button>
                </div>
              )}
            </div>
          )}

          {phase === 'result' && drill && (
            <div className="grammar-card">
              <p className="title num grammar-score">
                {formatInt(rightCount(drill))} of {formatInt(drill.cards.length)} right
              </p>
              {drill.missed.length > 0 && (
                <ul className="grammar-missed">
                  {drill.missed.map((missed, i) => (
                    <li key={i}>
                      {missed.before}
                      <strong>{missed.answer}</strong>
                      {missed.after}
                    </li>
                  ))}
                </ul>
              )}
              <div className="grammar-result-actions">
                <button type="button" className="btn btn-primary grammar-again" onClick={begin}>
                  Try again
                </button>
                <button type="button" className="btn btn-secondary grammar-leave" onClick={onBack}>
                  Back to topics
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  )
}

/// Before a pick every option is plain. After one the right option turns green and a wrong pick
/// red, reusing the sorting screen's known/unknown buttons, which the contrast test covers.
function optionClass(option: string, answer: string, picked: string | null): string {
  if (picked === null) {
    return 'btn-secondary'
  }

  if (option === answer) {
    return 'btn-known'
  }

  return option === picked ? 'btn-unknown' : 'btn-secondary'
}
