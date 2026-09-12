import type { DictionaryListItem } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { percentOf } from '../lib/format'
import './Sidebar.css'

interface Props {
  items: DictionaryListItem[] | null
  error: string | null
  activeId: number | null
  importActive: boolean
  canImport: boolean
  onSelect: (id: number) => void
  onImport: () => void
  verbsDoneSteps: number
  verbsTotalSteps: number
  verbsActive: boolean
  onOpenVerbs: () => void
}

export function Sidebar({
  items,
  error,
  activeId,
  importActive,
  canImport,
  onSelect,
  onImport,
  verbsDoneSteps,
  verbsTotalSteps,
  verbsActive,
  onOpenVerbs,
}: Props) {
  return (
    <nav className="sidebar" aria-label="Dictionaries">
      <p className="sidebar-heading">Dictionaries</p>

      {error && <p className="sidebar-note error">{error}</p>}
      {!items && !error && <p className="sidebar-note">Loading…</p>}
      {items?.length === 0 && <p className="sidebar-note">No dictionaries yet</p>}

      <ul className="sidebar-list">
        {items?.map((item) => (
          <li key={item.id}>
            <button
              type="button"
              className="sidebar-item"
              aria-current={item.id === activeId ? 'page' : undefined}
              onClick={() => onSelect(item.id)}
            >
              <span className="name">{item.name}</span>
              <span className="pct num">{percentOf(item.sortedCount, item.wordsCount)}%</span>
              <ProgressBar sorted={item.sortedCount} total={item.wordsCount} showLabel={false} />
            </button>
          </li>
        ))}
      </ul>

      <p className="sidebar-heading">Programs</p>
      <ul className="sidebar-list">
        <li>
          <button
            type="button"
            className="sidebar-item program-item"
            aria-current={verbsActive ? 'page' : undefined}
            onClick={onOpenVerbs}
          >
            <span className="name">Irregular verbs</span>
            <span className="pct num">
              {verbsDoneSteps} of {verbsTotalSteps} steps
            </span>
          </button>
        </li>
      </ul>

      {canImport && (
        <div className="sidebar-footer">
          <button
            type="button"
            className={`btn ${importActive ? 'btn-primary' : 'btn-secondary'}`}
            onClick={onImport}
          >
            Import a book
          </button>
        </div>
      )}
    </nav>
  )
}
