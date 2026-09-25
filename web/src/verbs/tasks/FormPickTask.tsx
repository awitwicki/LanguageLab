import type { TaskDto } from '../../api/client'
import './tasks.css'

interface Props {
  task: TaskDto
  disabled: boolean
  onAnswer: (answer: string) => void
}

/** Splits "They [went] home early." into the plain text with the bracketed form underlined. */
function Marked({ text }: { text: string }) {
  const match = /^(.*)\[([^\]]+)\](.*)$/.exec(text)

  if (!match) {
    return <>{text}</>
  }

  const [, before, form, after] = match
  return (
    <>
      {before}
      <u>{form}</u>
      {after}
    </>
  )
}

export function FormPickTask({ task, disabled, onAnswer }: Props) {
  return (
    <div className="form-pick-task">
      <p className="form-pick-sentence">
        <Marked text={task.formPick!.sentence} />
      </p>
      <div className="form-pick-options">
        <button type="button" className="btn btn-secondary option" disabled={disabled} onClick={() => onAnswer('v2')}>
          <kbd>1</kbd>
          <span>Past Simple · V2</span>
        </button>
        <button type="button" className="btn btn-secondary option" disabled={disabled} onClick={() => onAnswer('v3')}>
          <kbd>2</kbd>
          <span>Present Perfect · V3</span>
        </button>
      </div>
    </div>
  )
}
