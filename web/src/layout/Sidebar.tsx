import type { DictionaryListItem } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'
import { percentOf } from '../lib/format'
import './Sidebar.css'

interface Props {
  items: DictionaryListItem[] | null
  error: string | null
  activeId: number | null
  importActive: boolean
  onSelect: (id: number) => void
  onImport: () => void
}

export function Sidebar({ items, error, activeId, importActive, onSelect, onImport }: Props) {
  return (
    <nav className="sidebar" aria-label="Словники">
      <p className="sidebar-heading">Словники</p>

      {error && <p className="sidebar-note error">{error}</p>}
      {!items && !error && <p className="sidebar-note">Завантажую…</p>}
      {items?.length === 0 && <p className="sidebar-note">Поки жодного словника</p>}

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

      <div className="sidebar-footer">
        <button
          type="button"
          className={`btn ${importActive ? 'btn-primary' : 'btn-secondary'}`}
          onClick={onImport}
        >
          Імпортувати книжку
        </button>
      </div>
    </nav>
  )
}
