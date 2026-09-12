import { useEffect } from 'react'
import type { RecentWord } from '../api/client'
import { SortingProgress } from '../components/SortingProgress'
import { formatInt, plural } from '../lib/format'
import { useSortingQueue } from '../sorting/useSortingQueue'
import './SortingScreen.css'

interface Props {
  dictionaryId: number
  dictionaryName: string
  chapterIds: number[] | null
  scopeTitle: string
  onBack: () => void
}

export function SortingScreen({ dictionaryId, dictionaryName, chapterIds, scopeTitle, onBack }: Props) {
  const { current, known, unknown, total, sorted, loaded, error, mark, undo } = useSortingQueue({
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
    <>
      <div className="sorting-nav">
        <button type="button" className="btn btn-quiet" onClick={onBack}>
          ‹ {dictionaryName}
        </button>
      </div>

      <SortingProgress scope={dictionaryName} title={scopeTitle} sorted={sorted} total={total} />

      {error && <p className="error">{error}</p>}

      <div className="sorter">
        <Column kind="unknown" title="Don't know" words={unknown} />

        <div className="card">
          {!loaded && <p className="footnote">Loading…</p>}

          {loaded && current && (
            <>
              {/* key міняє вузол на кожному слові — так спрацьовує анімація появи. */}
              <p key={current.wordPairId} className="card-word">
                {current.word}
              </p>
              <p className="card-translation">{current.translation}</p>
              {/* Слово, повернуте через undo, приходить без частоти — краще не
                  показати нічого, ніж збрехати «зустрічається 0 разів». */}
              {current.frequency > 0 ? (
                <p className="frequency num">
                  occurs {formatInt(current.frequency)}{' '}
                  {plural(current.frequency, 'time', 'times')}
                </p>
              ) : (
                <p className="frequency">&nbsp;</p>
              )}

              <div className="card-actions">
                <button type="button" className="btn btn-lg btn-unknown" onClick={() => mark('unknown')}>
                  Don't know <kbd>←</kbd>
                </button>
                <button type="button" className="btn btn-lg btn-known" onClick={() => mark('known')}>
                  Know <kbd>→</kbd>
                </button>
              </div>

              <div className="card-actions-quiet">
                <button type="button" className="btn btn-quiet" onClick={undo}>
                  Undo <kbd>↑</kbd>
                </button>
                <button type="button" className="btn btn-quiet" onClick={() => mark('excluded')}>
                  Exclude <kbd>↓</kbd>
                </button>
              </div>
            </>
          )}

          {loaded && !current && (
            <div className="card-done">
              <p className="title">All sorted</p>
              <p className="footnote">No words left in this set.</p>
              <button type="button" className="btn btn-secondary" onClick={onBack}>
                Back to dictionary
              </button>
            </div>
          )}
        </div>

        <Column kind="known" title="Know" words={known} />
      </div>
    </>
  )
}

function Column({ kind, title, words }: { kind: 'known' | 'unknown'; title: string; words: RecentWord[] }) {
  return (
    <aside className={`column ${kind}`}>
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
