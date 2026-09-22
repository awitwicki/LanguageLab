import type { ChangeEvent } from 'react'
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
  personalActive: boolean
  onOpenPersonal: () => void
}

const PERSONAL_NAME = 'My words'

/** The picker's value for the personal entry: it is listed before the dictionaries have loaded, when its id is still unknown. */
const PERSONAL_VALUE = 'personal'

export function Sidebar({
  items,
  error,
  activeId,
  importActive,
  canImport,
  onSelect,
  onImport,
  personalActive,
  onOpenPersonal,
}: Props) {
  // The personal dictionary is pinned above the books; it is in the same list because the
  // server keeps it as a dictionary, but it is never sorted, so it shows a count, not a bar.
  const personal = items?.find((item) => item.isPersonal) ?? null
  const books = items?.filter((item) => !item.isPersonal)

  // The personal dictionary's own screen has no id in the route, its exercises carry the id
  // without the flag — the user is "in" it either way.
  const personalCurrent = personalActive || (personal !== null && personal.id === activeId)
  const personalLabel = `${personal?.name ?? PERSONAL_NAME} · ${wordsLabel(personal?.wordsCount ?? 0)}`

  // An empty value keeps the placeholder on the screens that belong to no dictionary.
  const picked = personalCurrent ? PERSONAL_VALUE : String(activeId ?? '')

  const pick = (event: ChangeEvent<HTMLSelectElement>) => {
    const value = event.target.value

    if (value === PERSONAL_VALUE) {
      onOpenPersonal()
    } else {
      onSelect(Number(value))
    }
  }

  return (
    <nav className="sidebar" aria-label="Dictionaries">
      {/* Phones show this one picker in place of the lists below; the swap is in the stylesheet.
          A native select opens the system picker there, which a row of chips could not match. */}
      <div className="sidebar-picker">
        <select aria-label="Dictionary" value={picked} onChange={pick}>
          <option value="" disabled>
            Choose a dictionary
          </option>
          <option value={PERSONAL_VALUE}>{personalLabel}</option>
          {books?.map((item) => (
            <option key={item.id} value={String(item.id)}>
              {`${item.name} · ${percentOf(item.sortedCount, item.wordsCount)}%`}
            </option>
          ))}
        </select>
      </div>

      <ul className="sidebar-list">
        <li>
          <button
            type="button"
            className="sidebar-item personal-item"
            aria-current={personalCurrent ? 'page' : undefined}
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

      {canImport && (
        <div className="sidebar-footer">
          <button
            type="button"
            className={`btn ${importActive ? 'btn-primary' : 'btn-secondary'}`}
            aria-label="Import a book"
            onClick={onImport}
          >
            {/* The short label is what a phone shows: it shares the row with the picker. */}
            <span className="import-label">Import a book</span>
            <span className="import-label-short">Import</span>
          </button>
        </div>
      )}
    </nav>
  )
}
