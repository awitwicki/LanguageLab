import { useEffect, useState } from 'react'
import { api, type DictionaryListItem } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'

interface Props {
  onOpen: (id: number) => void
  onImport: () => void
}

export function HomeScreen({ onOpen, onImport }: Props) {
  const [items, setItems] = useState<DictionaryListItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.listDictionaries().then(setItems).catch((e) => setError(String(e)))
  }, [])

  return (
    <section className="screen">
      <header className="row">
        <h1>Словники</h1>
        <button onClick={onImport}>Імпорт книжки</button>
      </header>

      {error && <p className="error">{error}</p>}
      {!items && !error && <p>Завантажую…</p>}

      <ul className="dictionary-list">
        {items?.map((item) => (
          <li key={item.id}>
            <button className="dictionary-card" onClick={() => onOpen(item.id)}>
              <strong>{item.name}</strong>
              <span>{item.hasChapters ? 'книжка з главами' : 'плаский список'}</span>
              <ProgressBar sorted={item.sortedCount} total={item.wordsCount} />
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
