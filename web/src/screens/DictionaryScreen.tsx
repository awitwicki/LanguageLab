import { useEffect, useState } from 'react'
import { api, type DictionaryDetail } from '../api/client'
import { ProgressBar } from '../components/ProgressBar'

interface Props {
  id: number
  onSort: (chapterIds: number[] | null) => void
  onBack: () => void
}

export function DictionaryScreen({ id, onSort, onBack }: Props) {
  const [detail, setDetail] = useState<DictionaryDetail | null>(null)
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.getDictionary(id).then(setDetail).catch((e) => setError(String(e)))
  }, [id])

  const toggle = (chapterId: number) => {
    setSelected((current) => {
      const next = new Set(current)
      next.has(chapterId) ? next.delete(chapterId) : next.add(chapterId)
      return next
    })
  }

  if (error) {
    return <p className="error">{error}</p>
  }

  if (!detail) {
    return <p>Завантажую…</p>
  }

  return (
    <section className="screen">
      <header className="row">
        <button onClick={onBack}>← Словники</button>
        <h1>{detail.name}</h1>
      </header>

      <ProgressBar sorted={detail.sortedCount} total={detail.wordsCount} />

      <div className="row">
        <button onClick={() => onSort(null)}>Сортувати всю книжку</button>
        <button disabled={selected.size === 0} onClick={() => onSort([...selected])}>
          Сортувати обрані глави ({selected.size})
        </button>
      </div>

      <ul className="chapter-list">
        {detail.chapters.map((chapter) => (
          <li key={chapter.id}>
            <label>
              <input
                type="checkbox"
                checked={selected.has(chapter.id)}
                onChange={() => toggle(chapter.id)}
              />
              <span>{chapter.title || <em>без назви</em>}</span>
            </label>
            <ProgressBar sorted={chapter.sortedCount} total={chapter.wordsCount} />
          </li>
        ))}
      </ul>

      {detail.chapters.length === 0 && <p>Це плаский словник — глав немає.</p>}
    </section>
  )
}
