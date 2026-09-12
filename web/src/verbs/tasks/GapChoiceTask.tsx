import type { TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
}

export function GapChoiceTask({ task, disabled, onAnswer }: Props) {
  const { sentence, options } = task.gapChoice!

  return (
    <div className="gap-choice-task">
      <p className="gap-choice-sentence">{sentence}</p>
      <div className="gap-choice-options">
        {options.map((option, index) => (
          <button
            key={option}
            type="button"
            className="btn btn-secondary option"
            disabled={disabled}
            onClick={() => onAnswer(option)}
          >
            <kbd>{index + 1}</kbd>
            <span>{option}</span>
          </button>
        ))}
      </div>
    </div>
  )
}
