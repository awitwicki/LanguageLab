import './ScopeRow.css'

interface Props {
  /** The scope this row leads back to — a book, or a book and one of its chapters. */
  title: string
  /** The second line: what the row is about. Already formatted. */
  sub: string
  /** The action button's label, and what the whole row does when clicked. */
  action: string
  busy?: boolean
  onAction: () => void
}

/**
 * One row of the home screen's "pick it up again" lists: a scope, a line about it, and the
 * single action that reopens it. The whole row is that action — a big target for the only
 * move there is — with the labelled button beside it to say what a click will do, the same
 * split `ChapterRow` uses. Rendered as an `<li>`: put it in a `.scope-list`.
 */
export function ScopeRow({ title, sub, action, busy = false, onAction }: Props) {
  return (
    <li className="scope-row">
      <button type="button" className="scope-main" disabled={busy} onClick={onAction}>
        <span className="scope-text">
          <span className="scope-title">{title}</span>
          <span className="scope-sub footnote num">{sub}</span>
        </span>
      </button>
      <button
        type="button"
        className="btn btn-quiet scope-action"
        disabled={busy}
        aria-label={`${action}: ${title}`}
        onClick={onAction}
      >
        {action}
      </button>
    </li>
  )
}
