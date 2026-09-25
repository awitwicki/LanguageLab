import { useState } from 'react'
import type { TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
}

/**
 * Two fields, V2 and V3. Group 3 below Learning3 mirrors V2 into V3 as it is typed —
 * making the rule "second and third are the same" visible rather than told.
 */
export function TripleTypeTask({ task, disabled, onAnswer }: Props) {
  const { v1, autofillV3 } = task.tripleType!
  const [v2, setV2] = useState('')
  const [v3, setV3] = useState('')

  const changeV2 = (next: string) => {
    setV2(next)

    if (autofillV3) {
      setV3(next)
    }
  }

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    onAnswer(`${v2}|${v3}`)
  }

  return (
    <div className="triple-type-task">
      <p className="card-task-v1">{v1}</p>
      <form className="triple-type-form" onSubmit={submit}>
        <div className="triple-type-row">
          <label htmlFor="triple-v2">V2</label>
          <input
            id="triple-v2"
            className="triple-type-input"
            value={v2}
            disabled={disabled}
            autoFocus
            onChange={(e) => changeV2(e.target.value)}
          />
        </div>
        <div className="triple-type-row">
          <label htmlFor="triple-v3">V3</label>
          <input
            id="triple-v3"
            className={`triple-type-input${autofillV3 ? ' is-autofilled' : ''}`}
            value={v3}
            disabled={disabled}
            onChange={(e) => setV3(e.target.value)}
          />
        </div>
        <button type="submit" className="btn btn-primary" disabled={disabled}>
          Check <kbd>Enter</kbd>
        </button>
      </form>
    </div>
  )
}
