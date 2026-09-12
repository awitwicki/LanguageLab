import { useState } from 'react'
import type { MatchPair, TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
  /** The pair from the most recent wrong attempt, flashed red until the next answer. */
  lastWrongPair?: MatchPair | null
}

export function MatchTask({ task, disabled, onAnswer, lastWrongPair }: Props) {
  const { lefts, rights, matched } = task.match!
  const [selectedLeft, setSelectedLeft] = useState<string | null>(null)

  const isMatchedLeft = (left: string) => matched.some((m) => m.left === left)
  const isMatchedRight = (right: string) => matched.some((m) => m.right === right)

  const pickLeft = (left: string) => {
    if (disabled || isMatchedLeft(left)) return
    setSelectedLeft(left)
  }

  const pickRight = (right: string) => {
    if (disabled || isMatchedRight(right) || !selectedLeft) return
    onAnswer(`${selectedLeft}=${right}`)
    setSelectedLeft(null)
  }

  return (
    <div className="match-task">
      <p className="gap-choice-sentence">Match each verb with its {task.match!.form === 'v2' ? 'Past Simple' : 'Present Perfect'} form.</p>
      <div className="match-columns">
        <div className="match-column">
          {lefts.map((left) => (
            <button
              key={left}
              type="button"
              data-left={left}
              className={[
                'btn btn-secondary match-item',
                left === selectedLeft ? 'is-selected' : '',
                isMatchedLeft(left) ? 'is-matched' : '',
                lastWrongPair?.left === left ? 'is-wrong' : '',
              ]
                .filter(Boolean)
                .join(' ')}
              disabled={disabled || isMatchedLeft(left)}
              onClick={() => pickLeft(left)}
            >
              {left}
            </button>
          ))}
        </div>
        <div className="match-column">
          {rights.map((right) => (
            <button
              key={right}
              type="button"
              data-right={right}
              className={[
                'btn btn-secondary match-item',
                isMatchedRight(right) ? 'is-matched' : '',
                lastWrongPair?.right === right ? 'is-wrong' : '',
              ]
                .filter(Boolean)
                .join(' ')}
              disabled={disabled || isMatchedRight(right)}
              onClick={() => pickRight(right)}
            >
              {right}
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
