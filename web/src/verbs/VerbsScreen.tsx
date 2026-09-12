import { useEffect, useState } from 'react'
import { api, type FormState, type IrregularVerbsOverview, type StepView } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { formatInt } from '../lib/format'
import './VerbsScreen.css'

interface Props {
  onStartSession: (step: number) => void
}

const LEGEND: [FormState, string][] = [
  ['learned', 'learned'],
  ['learning', 'learning'],
  ['missed', 'missed'],
  ['unseen', 'not seen'],
]

const DOTS = [0, 1, 2]

/** Start until a single form has a grade; Review once every verb of the step is learned. */
function actionLabel(step: StepView): 'Start' | 'Continue' | 'Review' {
  if (step.learnedVerbs === step.totalVerbs) {
    return 'Review'
  }

  const started = step.verbs.some((v) => v.forms.some((f) => f.state !== 'unseen'))
  return started ? 'Continue' : 'Start'
}

export function VerbsScreen({ onStartSession }: Props) {
  const [overview, setOverview] = useState<IrregularVerbsOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Set<number>>(new Set())

  useEffect(() => {
    api
      .getIrregularVerbs()
      .then(setOverview)
      .catch((e) => setError(String(e)))
  }, [])

  const toggle = (step: number) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(step)) next.delete(step)
      else next.add(step)
      return next
    })

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!overview) {
    return <p className="footnote">Loading…</p>
  }

  return (
    <>
      <h1 className="large-title">Irregular verbs</h1>
      <p className="verbs-intro">Four groups by pattern — learn them as families, not alphabetically.</p>

      <div className="step-cards">
        {overview.steps.map((step) => (
          <section key={step.step} className="step-card">
            <div className="step-card-head">
              <h2 className="headline">
                Step {step.step} · {step.title}
              </h2>
              <div className="step-card-actions">
                <button
                  type="button"
                  className="btn btn-quiet step-table-toggle"
                  onClick={() => toggle(step.step)}
                  aria-expanded={expanded.has(step.step)}
                >
                  Table
                </button>
                <button type="button" className="btn btn-primary step-action" onClick={() => onStartSession(step.step)}>
                  {actionLabel(step)}
                </button>
              </div>
            </div>

            <ProgressBar sorted={step.learnedForms} total={step.totalForms} showLabel={false} />
            <p className="footnote num step-caption">
              {formatInt(step.learnedVerbs)} of {formatInt(step.totalVerbs)} verbs learned
            </p>

            {expanded.has(step.step) && (
              <>
                <table className="step-table">
                  <thead>
                    <tr>
                      <th>V1</th>
                      <th>V2</th>
                      <th>V3</th>
                      <th>Translation</th>
                    </tr>
                  </thead>
                  <tbody>
                    {step.verbs.map((verb) => (
                      <tr key={verb.v1} className={verb.learned ? 'is-learned' : undefined}>
                        {verb.forms.map((f) => (
                          <td key={f.form} className="verb-cell" data-state={f.state} title={`${f.streak} in a row`}>
                            <span className="verb-cell-form">{verb[f.form]}</span>
                            <span className="verb-dots" aria-hidden="true">
                              {DOTS.map((i) => (
                                <span key={i} className={`verb-dot${i < Math.min(f.streak, DOTS.length) ? ' is-on' : ''}`} />
                              ))}
                            </span>
                          </td>
                        ))}
                        <td className="verb-cell-translation">{verb.translation}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <ul className="verb-legend footnote">
                  {LEGEND.map(([state, label]) => (
                    <li key={state}>
                      <span className="verb-swatch" data-state={state} /> {label}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </section>
        ))}
      </div>

      <div className="verbs-notes footnote">
        <p>
          <strong>read</strong> is spelt the same in all three forms but pronounced differently: /riːd/ for the
          present, /red/ for the past forms.
        </p>
        <p>
          British English uses <strong>got</strong> for the past participle of get; American English uses{' '}
          <strong>gotten</strong>.
        </p>
        <p>
          Some steps hide mini-families worth learning together: -ought / -aught (bought, thought, caught); i → a → u
          (begin – began – begun, drink – drank – drunk); -ow → -ew → -own (know – knew – known, grow – grew – grown).
        </p>
      </div>
    </>
  )
}
