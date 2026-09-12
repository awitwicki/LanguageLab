import { useState } from 'react'
import type { TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
  hint: string | null
}

export function GapTypeTask({ task, disabled, onAnswer, hint }: Props) {
  const [value, setValue] = useState('')
  const { sentence, hint: v1 } = task.gapType!

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    onAnswer(value)
  }

  return (
    <div className="gap-type-task">
      <p className="gap-type-sentence">{sentence}</p>
      <p className="footnote">({v1})</p>
      <form className="gap-type-form" onSubmit={submit}>
        <input
          className="gap-type-input"
          value={value}
          disabled={disabled}
          autoFocus
          onChange={(e) => setValue(e.target.value)}
        />
        <button type="submit" className="btn btn-primary" disabled={disabled}>
          Check <kbd>Enter</kbd>
        </button>
      </form>
      {hint && <p className="neutral-hint">{hint}</p>}
    </div>
  )
}
