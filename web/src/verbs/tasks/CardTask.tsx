import type { TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
}

/** Wraps the suffix's position in the form with <mark>, so the shared pattern stands out. */
function Highlighted({ form, suffixes }: { form: string; suffixes: string[] }) {
  const suffix = suffixes.find((s) => form.endsWith(s))

  if (!suffix) {
    return <>{form}</>
  }

  return (
    <>
      {form.slice(0, form.length - suffix.length)}
      <mark>{suffix}</mark>
    </>
  )
}

export function CardTask({ task, disabled, onAnswer }: Props) {
  const card = task.card!
  const suffixes = task.verb.suffixes

  return (
    <div className="card-task">
      <p className="card-task-v1">{task.verb.v1}</p>
      <p className="card-task-forms">
        <Highlighted form={card.v2.join(' / ')} suffixes={suffixes} /> · <Highlighted form={card.v3.join(' / ')} suffixes={suffixes} />
      </p>
      <p className="card-task-translation">{task.verb.translation}</p>

      <ul className="card-task-examples">
        {card.examples.map((example, i) => (
          <li key={i}>{example.text.replace(/\[([^\]]+)\]/, '$1')}</li>
        ))}
      </ul>

      {card.note && <p className="card-task-note footnote">{card.note}</p>}

      <button type="button" className="btn btn-primary btn-lg" disabled={disabled} onClick={() => onAnswer('seen')}>
        Got it <kbd>Enter</kbd>
      </button>
    </div>
  )
}
