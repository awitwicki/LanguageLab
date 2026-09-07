import { useEffect } from 'react'
import { ProgressBar } from '../components/ProgressBar'
import { useSortingQueue } from '../sorting/useSortingQueue'

interface Props {
  dictionaryId: number
  chapterIds: number[] | null
  onBack: () => void
}

export function SortingScreen({ dictionaryId, chapterIds, onBack }: Props) {
  const { current, known, unknown, total, sorted, error, mark, undo } = useSortingQueue({
    dictionaryId,
    chapterIds,
  })

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // Стрілка вказує на колонку, в яку слово полетить.
      if (event.key === 'ArrowLeft') mark('unknown')
      else if (event.key === 'ArrowRight') mark('known')
      else if (event.key === 'ArrowDown') mark('excluded')
      else if (event.key === 'ArrowUp') undo()
      else return

      event.preventDefault()
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [mark, undo])

  return (
    <section className="screen">
      <header className="row">
        <button onClick={onBack}>← Назад</button>
        <ProgressBar sorted={sorted} total={total} />
      </header>

      {error && <p className="error">{error}</p>}

      <div className="sorter">
        <Column title="Не знаю" words={unknown} />

        <div className="card">
          {current ? (
            <>
              <p className="word">{current.word}</p>
              {/* Слово, повернуте через undo, приходить без частоти — краще не
                  показати нічого, ніж збрехати «зустрічається 0 разів». */}
              {current.frequency > 0 && (
                <p className="frequency">зустрічається {current.frequency} разів</p>
              )}
              <div className="row">
                <button onClick={() => mark('unknown')}>← Не знаю</button>
                <button onClick={() => mark('known')}>Знаю →</button>
              </div>
              <div className="row">
                <button onClick={undo}>↑ Скасувати</button>
                <button onClick={() => mark('excluded')}>↓ Виключити</button>
              </div>
            </>
          ) : (
            <p>Слова закінчились 🎉</p>
          )}
        </div>

        <Column title="Знаю" words={known} />
      </div>
    </section>
  )
}

function Column({ title, words }: { title: string; words: { wordPairId: number; word: string }[] }) {
  return (
    <aside className="column">
      <h2>{title}</h2>
      <ol>
        {words.map((word, index) => (
          // Найсвіжіше зверху й підсвічене: інакше undo відпрацьовує невидимо.
          <li key={word.wordPairId} className={index === 0 ? 'newest' : undefined}>
            {word.word}
          </li>
        ))}
      </ol>
    </aside>
  )
}
