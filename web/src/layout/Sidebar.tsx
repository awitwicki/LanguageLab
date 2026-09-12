import type { DictionaryListItem } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { percentOf, wordsLabel } from '../lib/format'
import './Sidebar.css'

interface Props {
  items: DictionaryListItem[] | null
  error: string | null
  activeId: number | null
  importActive: boolean
  canImport: boolean
  onSelect: (id: number) => void
  onImport: () => void
  verbsLearnedPercent: number
  verbsActive: boolean
  onOpenVerbs: () => void
  personalActive: boolean
  onOpenPersonal: () => void
}

const PERSONAL_NAME = 'My words'

export function Sidebar({
  items,
  error,
  activeId,
  importActive,
  canImport,
  onSelect,
  onImport,
  verbsLearnedPercent,
  verbsActive,
  onOpenVerbs,
  personalActive,
  onOpenPersonal,
}: Props) {
  // The personal dictionary is pinned above the books; it is in the same list because the
  // server keeps it as a dictionary, but it is never sorted, so it shows a count, not a bar.
  const personal = items?.find((item) => item.isPersonal) ?? null
  const books = items?.filter((item) => !item.isPersonal)

  return (
    <nav className="sidebar" aria-label="Dictionaries">
      <p className="sidebar-heading">{PERSONAL_NAME}</p>
      <ul className="sidebar-list">
        <li>
          <button
            type="button"
            className="sidebar-item personal-item"
            aria-current={personalActive ? 'page' : undefined}
            onClick={onOpenPersonal}
          >
            <span className="name">{personal?.name ?? PERSONAL_NAME}</span>
            <span className="pct num">{wordsLabel(personal?.wordsCount ?? 0)}</span>
          </button>
        </li>
      </ul>

      <p className="sidebar-heading">Dictionaries</p>

      {error && <p className="sidebar-note error">{error}</p>}
      {!items && !error && <p className="sidebar-note">Loading…</p>}
      {books?.length === 0 && <p className="sidebar-note">No dictionaries yet</p>}

      <ul className="sidebar-list">
        {books?.map((item) => (
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
            <span className="pct num">{verbsLearnedPercent}% learned</span>
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
