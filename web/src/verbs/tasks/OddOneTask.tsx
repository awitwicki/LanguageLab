import type { TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
}

export function OddOneTask({ task, disabled, onAnswer }: Props) {
  const { options } = task.oddOne!

  return (
    <div className="odd-one-task">
      <p className="gap-choice-sentence">Which one is from another group?</p>
      <div className="odd-one-options">
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
